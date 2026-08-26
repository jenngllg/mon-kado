using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions and locks for wishlist participant operations.
/// </summary>
/// <param name="context">The database context.</param>
public class WishlistParticipantTransactionFactory(MonKadoDbContext context)
    : IWishlistParticipantTransactionFactory
{
    /// <inheritdoc />
    public async Task<IWishlistParticipantTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        return new WishlistParticipantTransaction(transaction);
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
    public async Task<Guid> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var wishlist = await context.Wishlists
            .FromSqlInterpolated($"SELECT *, xmin FROM public.wishlists WHERE id = {wishlistId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        return wishlist.OwnerId;
    }
}
