using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for wishlist share links.
/// </summary>
/// <param name="context">The database context.</param>
public class WishlistShareLinkRepository(MonKadoDbContext context) : IWishlistShareLinkRepository
{
    /// <inheritdoc />
    public void Add(WishlistShareLink shareLink)
    {
        context.WishlistShareLinks.Add(shareLink);
    }

    /// <inheritdoc />
    public void Remove(WishlistShareLink shareLink)
    {
        context.WishlistShareLinks.Remove(shareLink);
    }

    /// <inheritdoc />
    public Task<WishlistShareLink?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return context.WishlistShareLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                shareLink => shareLink.Id == id,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistShareLink?> GetByWishlistIdAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return context.WishlistShareLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                shareLink => shareLink.WishlistId == wishlistId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistShareLink?> GetByWishlistIdForUpdateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return context.WishlistShareLinks.SingleOrDefaultAsync(
            shareLink => shareLink.WishlistId == wishlistId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SharedWishlistDetails?> GetSharedWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return context.Wishlists
            .AsNoTracking()
            .Where(wishlist => wishlist.Id == wishlistId)
            .Select(wishlist => new SharedWishlistDetails(
                wishlist.Id,
                context.Users
                    .Where(member => member.Id == wishlist.OwnerId)
                    .Select(member => member.DisplayName)
                    .Single(),
                wishlist.Name,
                wishlist.Occasion,
                wishlist.EventDate,
                wishlist.Message,
                context.Wishes
                    .AsNoTracking()
                    .Where(wish => wish.WishlistId == wishlist.Id)
                    .OrderBy(wish => wish.Position)
                    .ThenBy(wish => wish.Id)
                    .Select(wish => new SharedWishDetails(
                        wish.Id,
                        wish.Name,
                        wish.Url,
                        wish.Price,
                        wish.Quantity,
                        context.Set<GiftReservation>()
                            .Where(reservation => reservation.WishId == wish.Id)
                            .Select(reservation => (int?)reservation.Quantity)
                            .Sum() ?? 0,
                        null))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<SharedWishDetail?> GetSharedWishAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .AsNoTracking()
            .Where(wish =>
                wish.WishlistId == wishlistId &&
                wish.Id == wishId)
            .Select(wish => new SharedWishDetail(
                wish.Id,
                wish.Name,
                wish.Note,
                wish.Url,
                wish.Price,
                wish.Quantity,
                context.Set<GiftReservation>()
                    .Where(reservation => reservation.WishId == wish.Id)
                    .Select(reservation => (int?)reservation.Quantity)
                    .Sum() ?? 0,
                null))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
