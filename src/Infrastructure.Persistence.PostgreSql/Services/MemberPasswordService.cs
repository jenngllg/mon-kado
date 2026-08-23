using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Changes authenticated member passwords in PostgreSQL.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="userRepository">The member repository.</param>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="emailChangeRequestRepository">The member email change request repository.</param>
/// <param name="outboxRepository">The authentication email outbox repository.</param>
/// <param name="userManager">The Identity user manager.</param>
/// <param name="timeProvider">The time provider.</param>
public class MemberPasswordService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IAuthenticationSessionRepository sessionRepository,
    IMemberEmailChangeRequestRepository emailChangeRequestRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    UserManager<MonKadoUser> userManager,
    TimeProvider timeProvider) : IMemberPasswordService
{
    private static readonly HashSet<string> _passwordPolicyErrorCodes =
    [
        "PasswordTooShort",
        "PasswordTooLong",
        "PasswordRequiresUniqueChars",
        "PasswordRequiresNonAlphanumeric",
        "PasswordRequiresDigit",
        "PasswordRequiresLower",
        "PasswordRequiresUpper"
    ];

    /// <inheritdoc />
    public async Task<bool> ChangeAsync(
        Guid memberId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(
                token => ChangeOnceAsync(
                    memberId,
                    currentPassword,
                    newPassword,
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

    /// <summary>
    /// Changes the password and invalidates the member security state in one transaction.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the password was changed; otherwise, <see langword="false" />.</returns>
    private async Task<bool> ChangeOnceAsync(
        Guid memberId,
        string currentPassword,
        string newPassword,
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

        var result = await userManager.ChangePasswordAsync(
            member,
            currentPassword,
            newPassword);

        if (result.Succeeded is false)
        {
            await transaction.RollbackAsync(cancellationToken);

            throw CreateIdentityFailure(result);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var emailChangeRequest = await emailChangeRequestRepository
            .GetActiveByUserIdForUpdateAsync(
                member.Id,
                cancellationToken);

        if (emailChangeRequest is not null)
        {
            emailChangeRequest.Revoke(now);
            await outboxRepository.MarkPendingEmailChangeMessagesProcessedAsync(
                emailChangeRequest.Id,
                now,
                cancellationToken);
        }

        await sessionRepository.RevokeAllForUserAsync(
            member.Id,
            now,
            cancellationToken);
        ArgumentNullException.ThrowIfNull(member.Email);
        outboxRepository.Add(
            AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
                member.Id,
                member.Email,
                now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Translates an Identity password change failure into an application exception.
    /// </summary>
    /// <param name="result">The failed Identity result.</param>
    /// <returns>The application exception representing the failure.</returns>
    private static Exception CreateIdentityFailure(IdentityResult result)
    {
        var errors = result.Errors.ToArray();

        if (errors.Any(error => error.Code == "PasswordMismatch"))
            return new CurrentPasswordInvalidException();

        var passwordErrors = errors
            .Where(error => IsPasswordPolicyError(error.Code))
            .Select(error => new ValidationError(
                "newPassword",
                GetPasswordPolicyMessage(error)))
            .ToArray();

        if (passwordErrors.Length != 0)
            return new RequestValidationException(passwordErrors);

        return new InvalidOperationException("Identity could not change the member password.");
    }

    /// <summary>
    /// Determines whether an Identity error represents a password policy violation.
    /// </summary>
    /// <param name="code">The Identity error code.</param>
    /// <returns><see langword="true" /> for a password policy error; otherwise, <see langword="false" />.</returns>
    private static bool IsPasswordPolicyError(string code)
    {

        return _passwordPolicyErrorCodes.Contains(code);
    }

    /// <summary>
    /// Gets the client-facing validation message for a password policy error.
    /// </summary>
    /// <param name="error">The Identity password policy error.</param>
    /// <returns>The validation message.</returns>
    private static string GetPasswordPolicyMessage(IdentityError error)
    {

        return error.Code switch
        {
            "PasswordTooShort" => ValidationMessages.PasswordTooShort,
            "PasswordTooLong" => ValidationMessages.PasswordTooLong,
            _ => error.Description
        };
    }
}
