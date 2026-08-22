using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a request to update the current member profile.
/// </summary>
/// <param name="displayName">The requested display name.</param>
[ExcludeFromCodeCoverage]
public class UpdateMemberProfileRequest(string? displayName)
{
    /// <summary>
    /// Gets the requested display name.
    /// </summary>
    public string? DisplayName { get; } = displayName;
}
