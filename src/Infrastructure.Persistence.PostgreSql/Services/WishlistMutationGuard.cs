using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Fences owner mutations against suspension and account deletion.</summary>
/// <param name="context">The database context sharing the caller's transaction.</param>
public class WishlistMutationGuard(MonKadoDbContext context) : IWishlistMutationGuard
{
    /// <summary>Revalidates owner access while holding account-before-parent locks until commit.</summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the parent is writable and locked.</returns>
    /// <exception cref="InvalidOperationException">The caller has not started a transaction.</exception>
    /// <exception cref="InvalidAuthenticationSessionException">The member no longer exists.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to this owner.</exception>
    /// <exception cref="WishlistSuspendedException">An administrator has suspended the wishlist.</exception>
    public async Task LockAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Wishlist mutations require an explicit transaction.");

        var owners = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM public.users WHERE id = {ownerId} FOR KEY SHARE
            """)
            .ToArrayAsync(cancellationToken);

        if (owners.Length == 0)
            throw new InvalidAuthenticationSessionException();

        var wishlist = await context.Wishlists
            .FromSqlInterpolated($"""
                SELECT wishlist.*, wishlist.xmin FROM public.wishlists AS wishlist
                WHERE wishlist.id = {wishlistId} AND wishlist.owner_id = {ownerId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (wishlist is null)
            throw new WishlistNotFoundException();

        if (wishlist.IsSuspended)
            throw new WishlistSuspendedException();
    }
}
