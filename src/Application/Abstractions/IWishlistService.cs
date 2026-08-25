using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates, updates, deletes and retrieves private wishlists.
/// </summary>
public interface IWishlistService
{
    /// <summary>
    /// Creates a private wishlist for an existing member.
    /// </summary>
    /// <param name="id">The generated wishlist identifier.</param>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="name">The normalized display name.</param>
    /// <param name="normalizedName">The normalized uniqueness key.</param>
    /// <param name="occasion">The associated occasion.</param>
    /// <param name="eventDate">The optional event date.</param>
    /// <param name="message">The optional owner message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wishlist, or <see langword="null" /> when the member no longer exists.</returns>
    /// <exception cref="Common.Exceptions.WishlistNameAlreadyExistsException">
    /// The member already owns a wishlist with the normalized name.
    /// </exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a private wishlist owned by a member.
    /// </summary>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="name">The normalized display name.</param>
    /// <param name="normalizedName">The normalized uniqueness key.</param>
    /// <param name="occasion">The associated occasion.</param>
    /// <param name="eventDate">The optional event date.</param>
    /// <param name="message">The optional owner message.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated wishlist, or <see langword="null" /> when it is unavailable to the member.</returns>
    /// <exception cref="Common.Exceptions.RequestValidationException">A changed event date is in the past.</exception>
    /// <exception cref="Common.Exceptions.WishlistNameAlreadyExistsException">
    /// The member already owns another wishlist with the normalized name.
    /// </exception>
    /// <exception cref="Common.Exceptions.WishlistVersionConflictException">The supplied version is stale.</exception>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">
    /// The member was deleted during the update.
    /// </exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a private wishlist owned by a member.
    /// </summary>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the wishlist was deleted; otherwise, <see langword="false" />.</returns>
    /// <exception cref="Common.Exceptions.WishlistVersionConflictException">The supplied version is stale.</exception>
    /// <exception cref="Common.Exceptions.InvalidAuthenticationSessionException">
    /// The member was deleted during the deletion.
    /// </exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<bool> DeleteAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a wishlist by identifier.
    /// </summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wishlist when found; otherwise, <see langword="null" />.</returns>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistDetails?> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all private wishlists owned by a member.
    /// </summary>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The ordered wishlists, an empty collection when the member owns none, or
    /// <see langword="null" /> when the member no longer exists.
    /// </returns>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<IReadOnlyCollection<WishlistDetails>?> GetByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the access of a member to a private wishlist.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owner access result.</returns>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistAccess> GetAccessAsync(
        Guid memberId,
        Guid wishlistId,
        CancellationToken cancellationToken);
}
