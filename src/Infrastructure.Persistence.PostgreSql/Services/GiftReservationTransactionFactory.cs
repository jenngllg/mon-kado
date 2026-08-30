using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions and row locks for reservation mutations.
/// </summary>
/// <param name="context">The database context.</param>
public class GiftReservationTransactionFactory(MonKadoDbContext context)
    : IGiftReservationTransactionFactory
{
    /// <inheritdoc />
    public async Task<IGiftReservationTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        return new GiftReservationTransaction(transaction);
    }

    /// <inheritdoc />
    public Task<WishlistShareLink?> LockShareLinkAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        return context.WishlistShareLinks
            .FromSqlInterpolated($"SELECT *, xmin FROM public.wishlist_share_links WHERE id = {shareLinkId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Wish?> LockWishAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .FromSqlInterpolated($"""
                SELECT wish.*, wish.xmin
                FROM public.wishes AS wish
                WHERE wish.wishlist_id = {wishlistId} AND wish.id = {wishId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
