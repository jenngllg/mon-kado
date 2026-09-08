using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>Fences notification attempts and atomically discards terminal recipients.</summary>
/// <param name="context">The scoped database context.</param>
public class AccountErasureEmailRepository(MonKadoDbContext context) : IAccountErasureEmailRepository
{
    /// <inheritdoc/>
    public async Task<AccountErasureEmailClaim?> ClaimAsync(
        DateTime now,
        TimeSpan leaseDuration,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.CreateVersion7();
        var lockedUntil = now.Add(leaseDuration);
        var rows = await context.AccountErasureEmails
            .FromSqlInterpolated($"""
                WITH candidate AS MATERIALIZED (
                    SELECT id FROM public.account_erasure_email_outbox
                    WHERE available_at <= {now} AND expires_at > {now}
                        AND attempt_count < {maximumAttempts}
                        AND (locked_until IS NULL OR locked_until <= {now})
                    ORDER BY available_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
                )
                UPDATE public.account_erasure_email_outbox AS message
                SET lease_id = {leaseId}, locked_until = LEAST({lockedUntil}, expires_at),
                    attempt_count = attempt_count + 1
                FROM candidate WHERE message.id = candidate.id
                RETURNING message.*
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var claimed = rows.SingleOrDefault();

        if (claimed is null)
            return null;

        return new AccountErasureEmailClaim
        {
            OperationId = claimed.Id,
            LeaseId = claimed.LeaseId.GetValueOrDefault(),
            AttemptCount = claimed.AttemptCount,
            LockedUntil = claimed.LockedUntil.GetValueOrDefault(),
            ExpiresAt = claimed.ExpiresAt,
            CreatedAt = claimed.CreatedAt,
            ProtectedRecipient = claimed.ProtectedRecipient
        };
    }

    /// <inheritdoc/>
    public async Task<bool> CompleteAsync(
        AccountErasureEmailClaim claim,
        DateTime now,
        AccountErasureNotificationStatus status,
        DateTime retryAt,
        CancellationToken cancellationToken)
    {

        if (status is AccountErasureNotificationStatus.Pending)
        {
            var updated = await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public.account_erasure_email_outbox
                SET available_at = {retryAt}, lease_id = NULL, locked_until = NULL
                WHERE id = {claim.OperationId} AND lease_id = {claim.LeaseId}
                    AND locked_until > {now} AND expires_at > {now}
                """,
                cancellationToken);

            return updated == 1;
        }

        var completed = await context.Database.ExecuteSqlInterpolatedAsync($"""
            WITH removed AS (
                DELETE FROM public.account_erasure_email_outbox
                WHERE id = {claim.OperationId} AND lease_id = {claim.LeaseId}
                    AND locked_until > {now} AND expires_at > {now}
                RETURNING id
            )
            UPDATE public.administrative_account_erasure_events AS audit
            SET notification_status = {status.ToString()}
            FROM removed WHERE audit.id = removed.id
            """,
            cancellationToken);

        return completed == 1;
    }
}
