using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>Serializes per-wishlist delivery while allowing independent worker instances.</summary>
/// <param name="context">The scoped database context.</param>
public class WishlistModerationEmailRepository(MonKadoDbContext context) : IWishlistModerationEmailRepository
{
    /// <inheritdoc />
    public async Task<WishlistModerationEmailClaim?> ClaimAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.CreateVersion7();
        var lockedUntil = now.Add(leaseDuration);
        var messages = await context.WishlistModerationEmails
            .FromSqlInterpolated($"""
                WITH candidate AS MATERIALIZED (
                    SELECT message.id
                    FROM public.wishlist_moderation_email_outbox AS message
                    JOIN public.wishlist_moderation_events AS decision ON decision.id = message.id
                    WHERE message.processed_at IS NULL AND message.available_at <= {now}
                        AND (message.locked_until IS NULL OR message.locked_until <= {now})
                        AND NOT EXISTS (
                            SELECT 1 FROM public.wishlist_moderation_email_outbox AS earlier_message
                            JOIN public.wishlist_moderation_events AS earlier ON earlier.id = earlier_message.id
                            WHERE earlier.wishlist_id = decision.wishlist_id AND earlier.sequence < decision.sequence
                                AND earlier_message.processed_at IS NULL)
                    ORDER BY decision.occurred_at, decision.id
                    LIMIT 1 FOR UPDATE OF message SKIP LOCKED
                )
                UPDATE public.wishlist_moderation_email_outbox AS message
                SET lease_id = {leaseId}, locked_until = {lockedUntil}, attempt_count = attempt_count + 1
                FROM candidate WHERE message.id = candidate.id
                RETURNING message.*
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var claimed = messages.SingleOrDefault();

        if (claimed is null)
            return null;

        return new WishlistModerationEmailClaim
        {
            EventId = claimed.Id,
            LeaseId = leaseId,
            AttemptCount = claimed.AttemptCount,
            LockedUntil = lockedUntil
        };
    }

    /// <inheritdoc />
    public Task<WishlistModerationEmailMessage?> GetMessageAsync(
        WishlistModerationEmailClaim claim,
        DateTime now,
        CancellationToken cancellationToken)
    {

        return context.WishlistModerationEmails
            .AsNoTracking()
            .Where(message => message.Id == claim.EventId && message.LeaseId == claim.LeaseId &&
                message.LockedUntil > now && message.ProcessedAt == null)
            .Join(
                context.WishlistModerationEvents,
                message => message.Id,
                decision => decision.Id,
                (
                    message,
                    decision) => decision)
            .Join(
                context.Wishlists,
                decision => decision.WishlistId,
                wishlist => wishlist.Id,
                (
                    decision,
                    wishlist) => new
                    {
                        Decision = decision,
                        Wishlist = wishlist
                    })
            .Join(
                context.Users.Where(member => member.EmailConfirmed && member.Email != null),
                value => value.Wishlist.OwnerId,
                member => member.Id,
                (
                    value,
                    member) => new WishlistModerationEmailMessage
                    {
                        EventId = value.Decision.Id,
                        WishlistId = value.Wishlist.Id,
                        WishlistName = value.Wishlist.Name,
                        RecipientAddress = member.Email ?? string.Empty,
                        Action = value.Decision.Action,
                        Reason = value.Decision.Reason,
                        OccurredAt = value.Decision.OccurredAt
                    })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        WishlistModerationEmailClaim claim,
        DateTime now,
        bool isTerminal,
        DateTime retryAt,
        string? failure,
        CancellationToken cancellationToken)
    {
        var processedAt = isTerminal ? now : (DateTime?)null;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public.wishlist_moderation_email_outbox
            SET processed_at = {processedAt}, available_at = {retryAt}, last_error = {failure},
                lease_id = NULL, locked_until = NULL
            WHERE id = {claim.EventId} AND lease_id = {claim.LeaseId}
                AND locked_until > {now} AND processed_at IS NULL
            """,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task PurgeAsync(
        DateTime processedBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            WITH expired AS (
                SELECT id FROM public.wishlist_moderation_email_outbox
                WHERE processed_at < {processedBefore}
                ORDER BY processed_at, id LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            DELETE FROM public.wishlist_moderation_email_outbox AS message
            USING expired WHERE message.id = expired.id
            """,
            cancellationToken);
    }
}
