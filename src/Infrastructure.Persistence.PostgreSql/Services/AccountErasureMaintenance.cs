using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Options;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Purges sensitive delivery state even while the email provider is disabled.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="options">The bounded cleanup policy.</param>
/// <param name="timeProvider">The UTC clock.</param>
/// <param name="logger">The technical outcome logger.</param>
public class AccountErasureMaintenance(
    MonKadoDbContext context,
    IOptions<AccountErasureProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountErasureMaintenance> logger) : IAccountErasureMaintenance
{
    /// <inheritdoc/>
    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var batchSize = options.Value.BatchSize;
        var maximumAttempts = options.Value.MaximumAttempts;
        var abandoned = await context.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidates AS MATERIALIZED (
                SELECT id FROM public.account_erasure_email_outbox
                WHERE expires_at <= {now} OR (attempt_count >= {maximumAttempts}
                    AND (locked_until IS NULL OR locked_until <= {now}))
                ORDER BY expires_at, id LIMIT {batchSize} FOR UPDATE SKIP LOCKED
            ), removed AS (
                DELETE FROM public.account_erasure_email_outbox AS message
                USING candidates WHERE message.id = candidates.id RETURNING message.id
            )
            UPDATE public.administrative_account_erasure_events AS audit
            SET notification_status = 'Failed'
            FROM removed WHERE audit.id = removed.id
            """,
            cancellationToken);

        if (abandoned > 0)
            AdministrativeAccountErasureLogMessages.NotificationsAbandoned(
                logger,
                abandoned);

        var expired = context.AdministrativeAccountErasureEvents
            .Where(audit => audit.CreatedAt.AddMonths(AdministrativeAccountErasureConstraints.AuditRetentionMonths) <= now)
            .OrderBy(audit => audit.CreatedAt)
            .ThenBy(audit => audit.Id)
            .Take(batchSize)
            .Select(audit => audit.Id);
        await context.AdministrativeAccountErasureEvents
            .Where(audit => expired.Contains(audit.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
