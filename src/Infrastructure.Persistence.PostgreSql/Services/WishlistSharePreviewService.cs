using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Projects only title and bounded image references of currently public lists.</summary>
/// <param name="context">The scoped database context.</param>
public class WishlistSharePreviewService(MonKadoDbContext context) : IWishlistSharePreviewService
{
    private const int MaximumPreviewImages = 2;

    /// <inheritdoc />
    public async Task<WishlistSharePreview?> GetAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        try
        {

            return await context.WishlistShareLinks
                .AsNoTracking()
                .Where(link => link.Id == shareLinkId)
                .Join(
                    context.Wishlists.AsNoTracking(),
                    link => link.WishlistId,
                    wishlist => wishlist.Id,
                    (link, wishlist) => wishlist)
                .Where(wishlist => !wishlist.IsArchived && !wishlist.IsSuspended &&
                    context.Users.Any(user => user.Id == wishlist.OwnerId && user.EmailConfirmed))
                .Select(wishlist => new WishlistSharePreview
                {
                    WishlistId = wishlist.Id,
                    Name = wishlist.Name,
                    Images = context.Wishes
                        .Where(wish => wish.WishlistId == wishlist.Id && wish.ImageId != null)
                        .OrderBy(wish => wish.Position)
                        .ThenBy(wish => wish.Id)
                        .Take(MaximumPreviewImages)
                        .Select(wish => new WishlistSharePreviewImage
                        {
                            WishId = wish.Id,
                            ImageId = wish.ImageId.GetValueOrDefault()
                        })
                        .ToArray()
                })
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyNames.PostgreSql,
                exception);
        }
    }
}
