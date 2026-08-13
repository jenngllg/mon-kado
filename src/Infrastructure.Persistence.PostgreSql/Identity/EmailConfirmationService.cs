using System.Text;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class EmailConfirmationService(
    MonKadoDbContext context,
    UserManager<MonKadoUser> userManager,
    ILookupNormalizer lookupNormalizer,
    TimeProvider timeProvider) : IEmailConfirmationService
{
    private const string ConcurrencyFailureErrorCode = "ConcurrencyFailure";
    private const string PendingOutboxConstraintName =
        "ux_authentication_email_outbox_pending_user_kind";
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RequestWindow = TimeSpan.FromHours(1);
    private const int MaximumRequestsPerWindow = 5;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<bool> ConfirmAsync(
        string userId,
        string token,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(userId, "D", out Guid parsedUserId) ||
            parsedUserId == Guid.Empty ||
            !TryDecodeToken(token, out string decodedToken))
        {
            return false;
        }

        try
        {
            IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
                await ConfirmOnce(parsedUserId, decodedToken, cancellationToken));
        }
        catch (Exception exception) when (IsPostgreSqlUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    public async Task RequestAsync(string email, CancellationToken cancellationToken)
    {
        string? normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return;
        }

        try
        {
            IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(async () =>
                await RequestOnce(normalizedEmail, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsPendingOutboxDuplicate(exception))
        {
            // Concurrent requests remain indistinguishable and create at most one pending message.
        }
        catch (Exception exception) when (IsPostgreSqlUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    private async Task<bool> ConfirmOnce(
        Guid userId,
        string decodedToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        bool concurrencyFailure = false;

        await using (IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken))
        {
            MonKadoUser? user = await context.Users.SingleOrDefaultAsync(
                candidate => candidate.Id == userId,
                cancellationToken);
            if (user is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (!user.EmailConfirmed &&
                (user.UnconfirmedAccountExpiresAt is null ||
                 user.UnconfirmedAccountExpiresAt <= now))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            bool tokenIsValid = await userManager.VerifyUserTokenAsync(
                user,
                userManager.Options.Tokens.EmailConfirmationTokenProvider,
                UserManager<MonKadoUser>.ConfirmEmailTokenPurpose,
                decodedToken);
            if (!tokenIsValid)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (user.EmailConfirmed)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            user.UnconfirmedAccountExpiresAt = null;
            user.UpdatedAt = now;
            user.Version++;
            IdentityResult result = await userManager.ConfirmEmailAsync(user, decodedToken);
            if (!result.Succeeded)
            {
                concurrencyFailure = result.Errors.Any(error =>
                    error.Code == ConcurrencyFailureErrorCode);
                await transaction.RollbackAsync(cancellationToken);
            }
            else
            {
                await context.AuthenticationEmailOutboxMessages
                    .Where(message =>
                        message.UserId == user.Id &&
                        message.Kind == AuthenticationEmailKind.EmailConfirmation &&
                        message.ProcessedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(message => message.ProcessedAt, now)
                            .SetProperty(message => message.LockedUntil, (DateTimeOffset?)null),
                        cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
        }

        if (!concurrencyFailure)
        {
            return false;
        }

        context.ChangeTracker.Clear();
        MonKadoUser? concurrentUser = await context.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);
        return concurrentUser?.EmailConfirmed == true &&
            await userManager.VerifyUserTokenAsync(
                concurrentUser,
                userManager.Options.Tokens.EmailConfirmationTokenProvider,
                UserManager<MonKadoUser>.ConfirmEmailTokenPurpose,
                decodedToken);
    }

    private async Task RequestOnce(string normalizedEmail, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        MonKadoUser? user = await context.Users
            .FromSqlInterpolated(
                $"SELECT * FROM public.users WHERE normalized_email = {normalizedEmail} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (user is null ||
            user.EmailConfirmed ||
            user.UnconfirmedAccountExpiresAt is null ||
            user.UnconfirmedAccountExpiresAt <= now)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        bool pendingRequestExists = await context.AuthenticationEmailOutboxMessages.AnyAsync(
            message =>
                message.UserId == user.Id &&
                message.Kind == AuthenticationEmailKind.EmailConfirmation &&
                message.ProcessedAt == null,
            cancellationToken);
        if (pendingRequestExists)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        DateTimeOffset windowStart = now.Subtract(RequestWindow);
        EmailRequestStatistics? statistics = await context.AuthenticationEmailOutboxMessages
            .Where(message =>
                message.UserId == user.Id &&
                message.Kind == AuthenticationEmailKind.EmailConfirmation &&
                message.CreatedAt >= windowStart)
            .GroupBy(_ => 1)
            .Select(group => new EmailRequestStatistics(
                group.Count(),
                group.Max(message => message.CreatedAt)))
            .SingleOrDefaultAsync(cancellationToken);

        if (statistics is not null &&
            (statistics.Count >= MaximumRequestsPerWindow ||
             statistics.LatestRequestAt > now.Subtract(MinimumRequestInterval)))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        context.AuthenticationEmailOutboxMessages.Add(
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(user.Id, now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool TryDecodeToken(string token, out string decodedToken)
    {
        decodedToken = string.Empty;
        try
        {
            string base64 = token.Replace('-', '+').Replace('_', '/');
            int remainder = base64.Length % 4;
            if (remainder == 1)
            {
                return false;
            }

            if (remainder > 0)
            {
                base64 = base64.PadRight(base64.Length + 4 - remainder, '=');
            }

            decodedToken = StrictUtf8.GetString(Convert.FromBase64String(base64));
            return decodedToken.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsPostgreSqlUnavailable(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is TimeoutException ||
                current is NpgsqlException and not PostgresException)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool IsPendingOutboxDuplicate(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PendingOutboxConstraintName
        };
    }

    private sealed record EmailRequestStatistics(int Count, DateTimeOffset LatestRequestAt);
}
