using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the editable profile of a member.
/// </summary>
/// <param name="displayName">The member display name.</param>
/// <param name="version">The member profile version.</param>
[ExcludeFromCodeCoverage]
public class MemberProfile(
    string displayName,
    uint version)
{
    /// <summary>
    /// Gets the member display name.
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// Gets the member profile version used for optimistic concurrency.
    /// </summary>
    public uint Version { get; } = version;
}
