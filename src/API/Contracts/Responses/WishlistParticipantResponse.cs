using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the wishlist participant associated with the current caller.
/// </summary>
/// <param name="id">The participant identifier.</param>
/// <param name="displayName">The current display name.</param>
[ExcludeFromCodeCoverage]
public class WishlistParticipantResponse(
    Guid id,
    string displayName)
{
    /// <summary>Gets the participant identifier.</summary>
    public Guid Id { get; } = id;

    /// <summary>Gets the current display name.</summary>
    public string DisplayName { get; } = displayName;
}
