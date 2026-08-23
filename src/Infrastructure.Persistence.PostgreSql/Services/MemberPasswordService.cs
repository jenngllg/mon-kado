using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

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
    /// <exception cref="CurrentPasswordInvalidException">Thrown when the current password is invalid.</exception>
    /// <exception cref="RequestValidationException">Thrown when Identity rejects the new password.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Identity reports an unexpected failure.</exception>
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

            throw IdentityPasswordFailureTranslator.CreateChangeException(result);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var emailChangeRequest = await emailChangeRequestRepository
            .GetActiveByUserIdForUpdateAsync(
                member.Id,
                cancellationToken);
        Guid? revokedEmailChangeRequestId = null;

        if (emailChangeRequest is not null)
        {
            emailChangeRequest.Revoke(now);
            revokedEmailChangeRequestId = emailChangeRequest.Id;
        }

        await sessionRepository.RevokeAllForUserAsync(
            member.Id,
            now,
            cancellationToken);

        if (revokedEmailChangeRequestId is { } requestId)
            await outboxRepository.MarkPendingEmailChangeMessagesProcessedAsync(
                requestId,
                now,
                cancellationToken);

        await outboxRepository.MarkPendingPasswordResetMessagesProcessedAsync(
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

}
