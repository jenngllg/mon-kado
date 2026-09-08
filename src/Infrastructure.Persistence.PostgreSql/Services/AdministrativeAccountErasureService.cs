using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Commits erasure, its audit and the protected notification as one irreversible operation.</summary>
/// <param name="context">The transactional context.</param>
/// <param name="unitOfWork">The shared save coordinator.</param>
/// <param name="administratorAccess">The current PostgreSQL role reader.</param>
/// <param name="dataRemoval">The shared account deletion engine.</param>
/// <param name="recipientProtector">The operation-bound recipient protector.</param>
/// <param name="scopeFactory">The independent commit verification scope factory.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class AdministrativeAccountErasureService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IAdministratorAccessService administratorAccess,
    IMemberAccountDataRemovalService dataRemoval,
    IAccountErasureRecipientProtector recipientProtector,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IAdministrativeAccountErasureService
{
    /// <inheritdoc/>
    public async Task<Guid> ExecuteAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        CancellationToken cancellationToken)
    {
        AdministrativeAccountErasureEvent? attempted = null;
        try
        {

            return await ExecuteAndAuditAsync(
                administratorId,
                memberId,
                requestReference,
                audit => attempted = audit,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (attempted is not null && await IsConfirmedAsync(
                attempted,
                cancellationToken))
                return attempted.Id;

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Locks accounts and commits data removal together with audit and notification staging.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="requestReference">The normalized support reference.</param>
    /// <param name="onCommitAttempt">Records the event before attempting its commit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable erasure operation identifier.</returns>
    private async Task<Guid> ExecuteAndAuditAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        Action<AdministrativeAccountErasureEvent> onCommitAttempt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var identifiers = new[]
        {
            administratorId,
            memberId
        };
        var accounts = await context.Users
            .FromSqlInterpolated($"SELECT *, xmin FROM public.users WHERE id = ANY({identifiers}) ORDER BY id FOR UPDATE")
            .ToArrayAsync(cancellationToken);
        await EnsureAdministratorAsync(
            context,
            administratorAccess,
            administratorId,
            cancellationToken);

        if (administratorId == memberId)
            throw new AccountSelfErasureNotAllowedException();
        var member = accounts.SingleOrDefault(account => account.Id == memberId);

        if (member is null)
            throw new AccountErasureTargetNotFoundException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var recipient = member.EmailConfirmed ? member.Email : null;
        var notificationStatus = recipient is null ? AccountErasureNotificationStatus.NotApplicable : AccountErasureNotificationStatus.Pending;
        var audit = new AdministrativeAccountErasureEvent(
            administratorId,
            memberId,
            requestReference,
            now,
            notificationStatus);
        context.AdministrativeAccountErasureEvents.Add(audit);

        if (recipient is not null)
        {
            context.AccountErasureEmails.Add(new AccountErasureEmail(
                audit.Id,
                recipientProtector.Protect(
                    audit.Id,
                    recipient),
                now));
        }

        await dataRemoval.StageAsync(
            member,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        onCommitAttempt(audit);
        await transaction.CommitAsync(cancellationToken);

        return audit.Id;
    }

    /// <summary>Rechecks the current confirmed administrator independently of JWT claims.</summary>
    /// <param name="database">The current or verification context.</param>
    /// <param name="accessService">The matching scoped authorization reader.</param>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed once current access is established.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The actor is missing or no longer confirmed.</exception>
    /// <exception cref="AdministratorAccessDeniedException">The actor no longer has administrator access.</exception>
    private static async Task EnsureAdministratorAsync(
        MonKadoDbContext database,
        IAdministratorAccessService accessService,
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        var access = await accessService.GetAccessAsync(
            administratorId,
            cancellationToken);

        if (access is AdministratorAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not AdministratorAccess.Granted)
            throw new AdministratorAccessDeniedException();

        if (!await database.Users
            .AsNoTracking()
            .AnyAsync(
                member => member.Id == administratorId && member.EmailConfirmed,
                cancellationToken))
            throw new InvalidAuthenticationSessionException();
    }

    /// <summary>Confirms this exact operation rather than interpreting arbitrary account absence as success.</summary>
    /// <param name="attempted">The event submitted to the ambiguous commit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the exact event and target absence are durably established.</returns>
    private async Task<bool> IsConfirmedAsync(
        AdministrativeAccountErasureEvent attempted,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await EnsureAdministratorAsync(
                database,
                scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>(),
                attempted.AdministratorId.GetValueOrDefault(),
                cancellationToken);

            return await database.AdministrativeAccountErasureEvents
                .AsNoTracking()
                .AnyAsync(
                    audit => audit.Id == attempted.Id && audit.AdministratorId == attempted.AdministratorId && audit.MemberId == attempted.MemberId && audit.RequestReference == attempted.RequestReference && !database.Users.Any(member => member.Id == attempted.MemberId),
                    cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            return false;
        }
    }
}
