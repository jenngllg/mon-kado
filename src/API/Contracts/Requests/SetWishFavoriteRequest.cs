using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Changes only the owner's favorite preference.</summary>
/// <param name="isFavorite">The requested preference, required by validation.</param>
[ExcludeFromCodeCoverage]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SetWishFavoriteRequest(bool? isFavorite)
{
    /// <summary>Gets the requested favorite preference.</summary>
    public bool? IsFavorite { get; } = isFavorite;
}
