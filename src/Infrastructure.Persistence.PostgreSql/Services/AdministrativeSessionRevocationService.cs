using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Atomically revokes sessions and audits the operation without replaying ambiguous commits.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="unitOfWork">The shared save coordinator.</param>
/// <param name="sessions">The specialized refresh-session repository.</param>
/// <param name="administratorAccess">The current administrator permission reader.</param>
/// <param name="scopeFactory">The independent verification scope factory.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class AdministrativeSessionRevocationService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IAuthenticationSessionRepository sessions,
    IAdministratorAccessService administratorAccess,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IAdministrativeSessionRevocationService
{
    /// <inheritdoc/>
    public async Task<Guid> ExecuteAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.CreateVersion7(timeProvider
            .GetUtcNow()
            .UtcDateTime);
        var commitAttempted = false;
        try
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
                administratorAccess,
                administratorId,
                cancellationToken);

            if (administratorId == memberId)
                throw new AccountSelfSessionRevocationNotAllowedException();

            if (!accounts.Any(account => account.Id == memberId))
                throw new AccountSessionRevocationTargetNotFoundException();
            var now = timeProvider
                .GetUtcNow()
                .UtcDateTime;
            await sessions.RevokeAllForUserAsync(
                memberId,
                now,
                cancellationToken);
            context.AdministrativeSessionRevocationEvents.Add(new AdministrativeSessionRevocationEvent
            {
                Id = operationId,
                AdministratorId = administratorId,
                MemberId = memberId,
                RequestReference = requestReference,
                CreatedAt = now
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (!commitAttempted || !await IsConfirmedAsync(
                operationId,
                administratorId,
                cancellationToken))
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
        }

        return operationId;
    }

    /// <summary>Rechecks administrative access after acquiring the account locks.</summary>
    /// <param name="accessService">The current PostgreSQL permission reader.</param>
    /// <param name="administratorId">The caller identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A task completed once administrator access is confirmed.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The administrator no longer exists.</exception>
    /// <exception cref="AdministratorAccessDeniedException">The administrator role is absent.</exception>
    private static async Task EnsureAdministratorAsync(
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
    }

    /// <summary>Confirms this exact operation without revoking any later reconnection.</summary>
    /// <param name="operationId">The attempted audit identifier.</param>
    /// <param name="administratorId">The actor whose access is revalidated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the operation is durably recorded.</returns>
    private async Task<bool> IsConfirmedAsync(
        Guid operationId,
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            await EnsureAdministratorAsync(
                scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>(),
                administratorId,
                cancellationToken);
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            return await database.AdministrativeSessionRevocationEvents
                .AsNoTracking()
                .AnyAsync(
                entry => entry.Id == operationId && entry.AdministratorId == administratorId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            return false;
        }
    }
}
