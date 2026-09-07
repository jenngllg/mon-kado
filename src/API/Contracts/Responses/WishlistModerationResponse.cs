using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Contains administrator-only current wishlist moderation information.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationResponse
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }
    /// <summary>Gets whether the wishlist is suspended.</summary>
    public bool IsSuspended
    {
        get; init;
    }
    /// <summary>Gets the private reason for the current suspension.</summary>
    public string? SuspensionReason
    {
        get; init;
    }
    /// <summary>Gets the UTC start of the current suspension.</summary>
    public DateTime? SuspendedAt
    {
        get; init;
    }
}
