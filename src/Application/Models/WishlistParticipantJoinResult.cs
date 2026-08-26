using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the result of joining a shared wishlist.
/// </summary>
/// <param name="participant">The current participant.</param>
/// <param name="isCreated">Whether a participant was created.</param>
/// <param name="guestToken">The optional newly issued guest token.</param>
/// <param name="guestTokenExpiresAt">The optional absolute guest-token expiration.</param>
[ExcludeFromCodeCoverage]
public class WishlistParticipantJoinResult(
    WishlistParticipantDetails participant,
    bool isCreated,
    string? guestToken,
    DateTime? guestTokenExpiresAt)
{
    /// <summary>Gets the current participant.</summary>
    public WishlistParticipantDetails Participant { get; } = participant;

    /// <summary>Gets whether a participant was created.</summary>
    public bool IsCreated { get; } = isCreated;

    /// <summary>Gets the optional newly issued guest token.</summary>
    public string? GuestToken { get; } = guestToken;

    /// <summary>Gets the optional absolute guest-token expiration.</summary>
    public DateTime? GuestTokenExpiresAt { get; } = guestTokenExpiresAt;
}
