using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Carries the information required to join a shared wishlist.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishlistParticipantJoinRequest
{
    /// <summary>Gets the generated participant identifier.</summary>
    public Guid ParticipantId
    {
        get; init;
    }

    /// <summary>Gets the generated guest-session identifier.</summary>
    public Guid GuestSessionId
    {
        get; init;
    }

    /// <summary>Gets the shared wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }

    /// <summary>Gets the share-link identifier validated under the participant transaction.</summary>
    public Guid ShareLinkId
    {
        get; init;
    }

    /// <summary>Gets the presented share-link secret.</summary>
    public string ShareSecret
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the optional authenticated member identifier.</summary>
    public Guid? MemberId
    {
        get; init;
    }

    /// <summary>Gets the optional browser guest token.</summary>
    public string? GuestToken
    {
        get; init;
    }

    /// <summary>Gets the optional anonymous display name.</summary>
    public string? DisplayName
    {
        get; init;
    }
}
