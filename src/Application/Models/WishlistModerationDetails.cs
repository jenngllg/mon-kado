using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains the private current moderation state of a wishlist.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationDetails
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
    /// <summary>Gets the optimistic concurrency version.</summary>
    public uint Version
    {
        get; init;
    }
}
