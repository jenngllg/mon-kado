using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents optional anonymous details used to join a shared wishlist.
/// </summary>
/// <param name="displayName">The anonymous display name.</param>
[ExcludeFromCodeCoverage]
public class JoinSharedWishlistRequest(string? displayName)
{
    /// <summary>Gets the anonymous display name.</summary>
    public string? DisplayName { get; } = displayName;
}
