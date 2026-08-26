using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for wishlist participants.
/// </summary>
public interface IWishlistParticipantRepository
{
    /// <summary>Adds a participant to the current unit of work.</summary>
    /// <param name="participant">The participant.</param>
    void Add(WishlistParticipant participant);

    /// <summary>Removes a participant from the current unit of work.</summary>
    /// <param name="participant">The participant.</param>
    void Remove(WishlistParticipant participant);

    /// <summary>Gets a tracked member participant.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant when found.</returns>
    Task<WishlistParticipant?> GetByMemberForUpdateAsync(
        Guid wishlistId,
        Guid memberId,
        CancellationToken cancellationToken);

    /// <summary>Gets a tracked guest participant.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="guestSessionId">The guest session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant when found.</returns>
    Task<WishlistParticipant?> GetByGuestSessionForUpdateAsync(
        Guid wishlistId,
        Guid guestSessionId,
        CancellationToken cancellationToken);

    /// <summary>Gets the current display name of a member.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The display name when the member exists.</returns>
    Task<string?> GetMemberDisplayNameAsync(
        Guid memberId,
        CancellationToken cancellationToken);

    /// <summary>Counts active participants in a wishlist.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of active participants.</returns>
    Task<int> CountActiveAsync(
        Guid wishlistId,
        DateTime now,
        CancellationToken cancellationToken);
}
