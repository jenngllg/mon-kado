using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Contains the public identity and discoverable lists of a member.</summary>
[ExcludeFromCodeCoverage]
public class PublicMemberProfileResponse
{
    /// <summary>Gets the public member identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Gets the optional versioned public photo URL.</summary>
    public string? ProfileImageUrl
    {
        get; init;
    }
    /// <summary>Gets the actively shared lists, newest first.</summary>
    public IEnumerable<PublicMemberWishlistResponse> Wishlists { get; init; } = [];
}
