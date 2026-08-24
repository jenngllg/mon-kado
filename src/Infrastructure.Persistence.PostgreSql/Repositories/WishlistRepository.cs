using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for private wishlists.
/// </summary>
/// <param name="context">The database context.</param>
public class WishlistRepository(MonKadoDbContext context) : IWishlistRepository
{
    /// <inheritdoc />
    public void Add(Wishlist wishlist)
    {
        context.Wishlists.Add(wishlist);
    }

    /// <inheritdoc />
    public Task<Wishlist?> GetByIdAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return context.Wishlists
            .AsNoTracking()
            .SingleOrDefaultAsync(
                wishlist => wishlist.Id == wishlistId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WishlistAccess> GetAccessAsync(
        Guid memberId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var access = await context.Users
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(_ => context.Wishlists.Any(wishlist =>
                wishlist.Id == wishlistId &&
                wishlist.OwnerId == memberId))
            .Cast<bool?>()
            .SingleOrDefaultAsync(cancellationToken);

        if (access is null)
            return WishlistAccess.MemberNotFound;

        return access.Value
            ? WishlistAccess.Owner
            : WishlistAccess.NotOwned;
    }
}
