using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Exposes only the public identity needed to display a member search result.</summary>
/// <param name="id">The stable member identifier used for a locally generated avatar.</param>
/// <param name="displayName">The non-unique member display name.</param>
[ExcludeFromCodeCoverage]
public class UserSearchResponse(
    Guid id,
    string displayName)
{
    /// <summary>Gets the member identifier.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Gets the public profile-photo URL, or null for a generated avatar based on Id.</summary>
    public string? ProfileImageUrl
    {
        get; init;
    }
}
