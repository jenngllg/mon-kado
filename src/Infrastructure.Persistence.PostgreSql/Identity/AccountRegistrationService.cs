using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class AccountRegistrationService(
    MonKadoDbContext context,
    UserManager<MonKadoUser> userManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    TimeProvider timeProvider) : IAccountRegistrationService
{
    private const string DuplicateEmailErrorCode = "DuplicateEmail";
    private const string DuplicateUserNameErrorCode = "DuplicateUserName";
    private const string NormalizedEmailIndexName = "ux_users_normalized_email";
    private const string NormalizedUserNameIndexName = "ux_users_normalized_user_name";
    private static readonly TimeSpan UnconfirmedAccountLifetime = TimeSpan.FromDays(30);

    public async Task RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(async () =>
            {
                // A retry must not reuse tracked state left by an interrupted transaction.
                context.ChangeTracker.Clear();
                MonKadoUser? existingUser = await userManager.FindByEmailAsync(email);
                if (existingUser is not null)
                {
                    PerformTimingEqualizationHash(password);
                    return;
                }

                await using IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync(cancellationToken);

                DateTimeOffset now = timeProvider.GetUtcNow();
                MonKadoUser user = new()
                {
                    Id = Guid.CreateVersion7(now),
                    Email = email,
                    UserName = email,
                    DisplayName = displayName,
                    EmailConfirmed = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UnconfirmedAccountExpiresAt = now.Add(UnconfirmedAccountLifetime),
                    Version = 1
                };

                IdentityResult result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    if (IsDuplicateAccount(result))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return;
                    }

                    string errorCodes = string.Join(',', result.Errors.Select(error => error.Code));
                    throw new InvalidOperationException($"ASP.NET Core Identity rejected account creation: {errorCodes}.");
                }

                context.AuthenticationEmailOutboxMessages.Add(
                    AuthenticationEmailOutboxMessage.CreateEmailConfirmation(user.Id, now));
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException exception) when (IsDuplicateAccount(exception))
        {
            // A concurrent request committed the same normalized email first.
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    private void PerformTimingEqualizationHash(string password)
    {
        MonKadoUser dummyUser = new()
        {
            Id = Guid.Empty,
            UserName = "timing-equalization"
        };

        _ = passwordHasher.HashPassword(dummyUser, password);
    }

    private static bool IsDuplicateAccount(IdentityResult result)
    {
        return result.Errors.Any(error =>
            error.Code is DuplicateEmailErrorCode or DuplicateUserNameErrorCode);
    }

    private static bool IsDuplicateAccount(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NormalizedEmailIndexName or NormalizedUserNameIndexName
        };
    }
}
