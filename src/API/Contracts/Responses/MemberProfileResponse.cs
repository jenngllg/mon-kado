using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the editable profile of a member.
/// </summary>
/// <param name="displayName">The member display name.</param>
[ExcludeFromCodeCoverage]
public class MemberProfileResponse(string displayName)
{
    /// <summary>
    /// Gets the member display name.
    /// </summary>
    public string DisplayName { get; } = displayName;
}
