using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Manages owner share links and resolves their public wishlist content.
/// </summary>
public interface IWishlistShareService
{
    /// <summary>Creates the active share link of an owned wishlist.</summary>
    /// <param name="id">The generated share-link identifier.</param>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created share link, or <see langword="null" /> when unavailable.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is not owned by the member.</exception>
    /// <exception cref="WishlistShareLinkAlreadyExistsException">An active link already exists.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistShareLinkDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>Gets the active share link of an owned wishlist.</summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active share link, or <see langword="null" />.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is not owned by the member.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistShareLinkDetails?> GetAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>Rotates the active share-link secret.</summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated share link, or <see langword="null" />.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is not owned by the member.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">The expected version is stale.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistShareLinkDetails?> RotateAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Revokes the active share link.</summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when revoked.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is not owned by the member.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">The expected version is stale.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<bool> DeleteAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Gets public wishlist content from a share-link secret.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented secret.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public wishlist content, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<SharedWishlistDetails?> GetSharedAsync(
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken);

    /// <summary>Gets detailed public information about one gift wish from a share-link secret.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented secret.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The explicit public gift-wish lookup result.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<SharedWishLookupResult> GetSharedWishAsync(
        Guid shareLinkId,
        string secret,
        Guid wishId,
        CancellationToken cancellationToken);
}
