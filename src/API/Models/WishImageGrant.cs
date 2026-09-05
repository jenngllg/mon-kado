using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Models;

/// <summary>
/// Represents the authenticated payload of a short-lived gift-image URL.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishImageGrant
{
    /// <summary>Gets or initializes the protected payload version.</summary>
    public int Version
    {
        get; init;
    }

    /// <summary>Gets or initializes the access scope.</summary>
    public string? Scope
    {
        get; init;
    }

    /// <summary>Gets or initializes the optional owner identifier.</summary>
    public Guid? OwnerId
    {
        get; init;
    }

    /// <summary>Gets or initializes the optional share-link identifier.</summary>
    public Guid? ShareLinkId
    {
        get; init;
    }

    /// <summary>Gets or initializes the parent wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }

    /// <summary>Gets or initializes the gift-wish identifier.</summary>
    public Guid WishId
    {
        get; init;
    }

    /// <summary>Gets or initializes the immutable image identifier.</summary>
    public Guid ImageId
    {
        get; init;
    }

    /// <summary>Gets or initializes the UTC expiration date and time.</summary>
    public DateTime ExpiresAt
    {
        get; init;
    }
}
