using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the participant associated with the current caller.
/// </summary>
/// <param name="id">The participant identifier.</param>
/// <param name="displayName">The current display name.</param>
[ExcludeFromCodeCoverage]
public class WishlistParticipantDetails(
    Guid id,
    string displayName)
{
    /// <summary>Gets the participant identifier.</summary>
    public Guid Id { get; } = id;

    /// <summary>Gets the current display name.</summary>
    public string DisplayName { get; } = displayName;
}
