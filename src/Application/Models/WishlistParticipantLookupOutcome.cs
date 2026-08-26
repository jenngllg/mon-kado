namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Identifies the outcome of resolving the current wishlist participant.
/// </summary>
public enum WishlistParticipantLookupOutcome
{
    /// <summary>The current participant was found.</summary>
    Found,

    /// <summary>No member or guest identity was presented.</summary>
    MissingIdentity,

    /// <summary>The guest token is invalid or expired.</summary>
    InvalidGuestSession,

    /// <summary>The current identity has not joined the wishlist.</summary>
    NotJoined,

    /// <summary>The authenticated member no longer exists.</summary>
    MemberNotFound
}
