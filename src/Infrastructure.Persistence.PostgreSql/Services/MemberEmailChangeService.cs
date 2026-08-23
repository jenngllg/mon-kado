using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Manages authenticated member email changes in PostgreSQL.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="userRepository">The member repository.</param>
/// <param name="requestRepository">The member email change request repository.</param>
/// <param name="outboxRepository">The authentication email outbox repository.</param>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="userManager">The Identity user manager.</param>
/// <param name="passwordHasher">The Identity password hasher.</param>
/// <param name="lookupNormalizer">The Identity lookup normalizer.</param>
/// <param name="timeProvider">The time provider.</param>
public class MemberEmailChangeService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IMemberEmailChangeRequestRepository requestRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    IAuthenticationSessionRepository sessionRepository,
    UserManager<MonKadoUser> userManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    ILookupNormalizer lookupNormalizer,
    TimeProvider timeProvider) : IMemberEmailChangeService
{
    private static readonly TimeSpan _requestLifetime = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<bool> RequestAsync(
        Guid memberId,
        string email,
        string currentPassword,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(
                token => RequestOnceAsync(
                    memberId,
                    email,
                    currentPassword,
                    expectedVersion,
                    token),
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        Guid requestId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AuthenticationEmailTokenEncoding.TryDecode(
            token,
            out var decodedToken))
            return false;

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(
                currentToken => ConfirmOnceAsync(
                    requestId,
                    decodedToken,
                    currentToken),
                cancellationToken);
        }
        catch (MemberEmailAlreadyUsedException)
        {

            throw;
        }
        catch (DbUpdateConcurrencyException)
        {

            throw new MemberEmailChangeInvalidException();
        }
        catch (Exception exception) when (IsUniqueEmailViolation(exception))
        {

            throw new MemberEmailAlreadyUsedException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    private async Task<bool> RequestOnceAsync(
        Guid memberId,
        string email,
        string currentPassword,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var member = await userRepository.GetByIdForUpdateAsync(
            memberId,
            cancellationToken);

        if (member is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        if (member.Version != expectedVersion)
            throw new MemberProfileVersionConflictException();

        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        ArgumentNullException.ThrowIfNull(member.NormalizedEmail);

        if (string.Equals(
            member.NormalizedEmail,
            normalizedEmail,
            StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        var passwordHash = member.PasswordHash;
        var passwordIsValid = passwordHash is not null &&
            passwordHasher.VerifyHashedPassword(
                member,
                passwordHash,
                currentPassword) is not PasswordVerificationResult.Failed;

        if (!passwordIsValid)
            throw new CurrentPasswordInvalidException();

        var emailIsUsed = await userRepository.Query()
            .AnyAsync(
                candidate =>
                    candidate.Id != member.Id &&
                    candidate.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (emailIsUsed)
            throw new MemberEmailAlreadyUsedException();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var previousRequest = await requestRepository.GetActiveByUserIdForUpdateAsync(
            member.Id,
            cancellationToken);

        if (previousRequest is not null &&
            previousRequest.IsActive(now) &&
            string.Equals(
                previousRequest.NormalizedNewEmail,
                normalizedEmail,
                StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        if (previousRequest is not null)
        {
            previousRequest.Revoke(now);
            await outboxRepository.MarkPendingEmailChangeMessagesProcessedAsync(
                previousRequest.Id,
                now,
                cancellationToken);
        }

        ArgumentNullException.ThrowIfNull(member.Email);
        var request = MemberEmailChangeRequest.Create(
            member.Id,
            member.Email,
            email,
            normalizedEmail,
            now,
            now.Add(_requestLifetime));
        requestRepository.Add(request);
        outboxRepository.Add(AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
            request.Id,
            member.Id,
            email,
            now));
        outboxRepository.Add(AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
            request.Id,
            member.Id,
            member.Email,
            now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private async Task<bool> ConfirmOnceAsync(
        Guid requestId,
        string decodedToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var requestSnapshot = await requestRepository.GetByIdAsync(
            requestId,
            cancellationToken);

        if (requestSnapshot is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var member = await userRepository.GetByIdForUpdateAsync(
            requestSnapshot.UserId,
            cancellationToken);

        if (member is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var request = await requestRepository.GetByIdForUpdateAsync(
            requestId,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (request is null || !request.IsActive(now))
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        if (!string.Equals(
            member.Email,
            request.CurrentEmail,
            StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var purpose = MemberEmailChangeTokenPurpose.Create(
            request.Id,
            request.NormalizedNewEmail);
        var tokenIsValid = await userManager.VerifyUserTokenAsync(
            member,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose,
            decodedToken);

        if (!tokenIsValid)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var emailIsUsed = await userRepository.Query()
            .AnyAsync(
                candidate =>
                    candidate.Id != member.Id &&
                    candidate.NormalizedEmail == request.NormalizedNewEmail,
                cancellationToken);

        if (emailIsUsed)
            throw new MemberEmailAlreadyUsedException();

        var identityToken = await userManager.GenerateChangeEmailTokenAsync(
            member,
            request.NewEmail);
        member.UserName = request.NewEmail;
        request.Confirm(now);
        var result = await userManager.ChangeEmailAsync(
            member,
            request.NewEmail,
            identityToken);

        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (result.Errors.Any(error =>
                error.Code is "DuplicateEmail" or "DuplicateUserName"))
                throw new MemberEmailAlreadyUsedException();

            if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
                throw new MemberEmailChangeInvalidException();

            throw new InvalidOperationException("Identity could not apply the member email change.");
        }

        await sessionRepository.RevokeAllForUserAsync(
            member.Id,
            now,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static bool IsUniqueEmailViolation(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {

            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;

            current = current.InnerException;
        }

        return false;
    }
}
