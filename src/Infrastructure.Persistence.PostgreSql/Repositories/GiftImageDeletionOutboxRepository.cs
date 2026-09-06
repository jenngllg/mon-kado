using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL operations for obsolete gift-image deletions.
/// </summary>
/// <param name="context">The database context.</param>
public class GiftImageDeletionOutboxRepository(MonKadoDbContext context)
    : IGiftImageDeletionOutboxRepository
{
    /// <inheritdoc />
    public void Add(GiftImageDeletionOutboxMessage message)
    {
        context.GiftImageDeletionOutboxMessages.Add(message);
    }

    /// <inheritdoc />
    public Task<GiftImageDeletionOutboxMessage?> GetNextForUpdateAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        return context.GiftImageDeletionOutboxMessages
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM public.gift_image_deletion_outbox
                WHERE available_at <= {now}
                  AND (locked_until IS NULL OR locked_until <= {now})
                ORDER BY available_at, created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<GiftImageDeletionOutboxMessage?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return context.GiftImageDeletionOutboxMessages.SingleOrDefaultAsync(
            message => message.Id == id,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Remove(GiftImageDeletionOutboxMessage message)
    {
        context.GiftImageDeletionOutboxMessages.Remove(message);
    }
}
