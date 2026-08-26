using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Manages member and guest participation in shared wishlists.
/// </summary>
public interface IWishlistParticipantService
{
    /// <summary>Creates or resolves the participant associated with the current caller.</summary>
    /// <param name="request">The participant join request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The joined or existing participant.</returns>
    Task<WishlistParticipantJoinResult> JoinAsync(
        WishlistParticipantJoinRequest request,
        CancellationToken cancellationToken);

    /// <summary>Resolves the current participant for a wishlist.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant resolution outcome.</returns>
    Task<WishlistParticipantLookupResult> GetCurrentAsync(
        Guid wishlistId,
        Guid? memberId,
        string? guestToken,
        CancellationToken cancellationToken);
}
