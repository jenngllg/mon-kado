using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains only the public identity and actively shared lists of a confirmed member.</summary>
[ExcludeFromCodeCoverage]
public class PublicMemberProfile
{
    /// <summary>Gets the member identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the public display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Gets the optional current public photo identifier.</summary>
    public Guid? ProfileImageId
    {
        get; init;
    }
    /// <summary>Gets the discoverable lists, newest first.</summary>
    public IReadOnlyList<PublicMemberWishlist> Wishlists { get; init; } = [];
}
