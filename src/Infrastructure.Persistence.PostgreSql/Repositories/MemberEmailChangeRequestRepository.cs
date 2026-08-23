using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for member email change requests.
/// </summary>
/// <param name="context">The database context.</param>
public class MemberEmailChangeRequestRepository(MonKadoDbContext context)
    : IMemberEmailChangeRequestRepository
{
    /// <inheritdoc />
    public void Add(MemberEmailChangeRequest request)
    {
        context.MemberEmailChangeRequests.Add(request);
    }

    /// <inheritdoc />
    public Task<MemberEmailChangeRequest?> GetActiveByUserIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {

        return context.MemberEmailChangeRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.member_email_change_requests
                WHERE user_id = {userId}
                  AND confirmed_at IS NULL
                  AND revoked_at IS NULL
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MemberEmailChangeRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {

        return context.MemberEmailChangeRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.member_email_change_requests
                WHERE id = {requestId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MemberEmailChangeRequest?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {

        return context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                request => request.Id == requestId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredOrCompletedAsync(
        DateTime expirationCutoff,
        DateTime completedCutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var requestIds = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .Where(request =>
                ((request.ConfirmedAt == null &&
                  request.RevokedAt == null &&
                  request.ExpiresAt <= expirationCutoff) ||
                 request.ConfirmedAt <= completedCutoff ||
                 request.RevokedAt <= completedCutoff) &&
                !context.AuthenticationEmailOutboxMessages.Any(message =>
                    message.MemberEmailChangeRequestId == request.Id &&
                    message.ProcessedAt == null))
            .OrderBy(request => request.ExpiresAt)
            .ThenBy(request => request.Id)
            .Select(request => request.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        return requestIds.Length == 0
            ? 0
            : await context.MemberEmailChangeRequests
                .Where(request => requestIds.Contains(request.Id))
                .ExecuteDeleteAsync(cancellationToken);
    }
}
