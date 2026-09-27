using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Reads the public profile and active, non-suspended shared lists in one database query.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="tokenService">The existing share-token protection service.</param>
public class PublicMemberProfileService(
    MonKadoDbContext context,
    IWishlistShareTokenService tokenService) : IPublicMemberProfileService
{
    /// <inheritdoc/>
    public async Task<PublicMemberProfile?> GetAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var member = await context.Users
                .AsNoTracking()
                .Where(user => user.Id == memberId && user.EmailConfirmed)
                .Select(user => new
                {
                    user.Id,
                    user.DisplayName,
                    user.ProfileImageId,
                    Wishlists = context.Wishlists
                        .Where(wishlist => wishlist.OwnerId == user.Id && !wishlist.IsSuspended)
                        .Join(
                            context.WishlistShareLinks,
                            wishlist => wishlist.Id,
                            link => link.WishlistId,
                            (wishlist, link) => new
                            {
                                wishlist.Id,
                                wishlist.Name,
                                wishlist.Occasion,
                                wishlist.EventDate,
                                wishlist.CreatedAt,
                                ShareLinkId = link.Id,
                                link.ProtectedSecret
                            })
                        .OrderByDescending(wishlist => wishlist.CreatedAt)
                        .ThenBy(wishlist => wishlist.Id)
                        .ToArray()
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (member is null)
                return null;

            return new PublicMemberProfile
            {
                Id = member.Id,
                DisplayName = member.DisplayName,
                ProfileImageId = member.ProfileImageId,
                Wishlists = member.Wishlists
                    .Select(wishlist => new PublicMemberWishlist
                    {
                        Id = wishlist.Id,
                        Name = wishlist.Name,
                        Occasion = wishlist.Occasion,
                        EventDate = wishlist.EventDate,
                        ShareLinkId = wishlist.ShareLinkId,
                        Secret = tokenService.Unprotect(wishlist.ProtectedSecret)
                    })
                    .ToArray()
            };
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                DependencyNames.PostgreSql,
                exception);
        }
    }
}
