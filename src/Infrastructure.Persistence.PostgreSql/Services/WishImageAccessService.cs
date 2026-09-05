using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Revalidates signed gift-image grants against current PostgreSQL state.
/// </summary>
/// <param name="context">The database context.</param>
public class WishImageAccessService(MonKadoDbContext context) : IWishImageAccessService
{
    /// <inheritdoc />
    public async Task<bool> IsOwnedImageCurrentAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishes
                .AsNoTracking()
                .Join(
                    context.Wishlists.AsNoTracking(),
                    wish => wish.WishlistId,
                    wishlist => wishlist.Id,
                    (wish, wishlist) => new
                    {
                        Wish = wish,
                        Wishlist = wishlist
                    })
                .Where(result => result.Wishlist.OwnerId == ownerId &&
                    result.Wishlist.Id == wishlistId &&
                    result.Wish.Id == wishId &&
                    result.Wish.ImageId == imageId)
                .Select(result => result.Wish.Id)
                .AnyAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsSharedImageCurrentAsync(
        Guid shareLinkId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.WishlistShareLinks
                .AsNoTracking()
                .Join(
                    context.Wishes.AsNoTracking(),
                    shareLink => shareLink.WishlistId,
                    wish => wish.WishlistId,
                    (shareLink, wish) => new
                    {
                        ShareLink = shareLink,
                        Wish = wish
                    })
                .Where(result => result.ShareLink.Id == shareLinkId &&
                    result.ShareLink.WishlistId == wishlistId &&
                    result.Wish.Id == wishId &&
                    result.Wish.ImageId == imageId)
                .Select(result => result.Wish.Id)
                .AnyAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
