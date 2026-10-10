using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>Represents a member following a particular generation of a shared wishlist.</summary>
public class WishlistSubscription : IAuditableEntity
{
    private WishlistSubscription()
    {
    }

    /// <summary>Initializes a subscription to the verified share capability.</summary>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="memberId">The subscriber identifier.</param>
    /// <param name="wishlistId">The shared wishlist identifier.</param>
    /// <param name="shareLinkId">The verified share-link identifier.</param>
    /// <param name="shareSecretHash">The verified secret fingerprint, never the bearer secret.</param>
    public WishlistSubscription(
        Guid id,
        Guid memberId,
        Guid wishlistId,
        Guid shareLinkId,
        byte[] shareSecretHash)
    {
        Id = id;
        MemberId = memberId;
        WishlistId = wishlistId;
        ShareLinkId = shareLinkId;
        ShareSecretHash = shareSecretHash.ToArray();
    }

    /// <summary>Gets the subscription identifier.</summary>
    public Guid Id
    {
        get; private set;
    }
    /// <summary>Gets the subscriber identifier.</summary>
    public Guid MemberId
    {
        get; private set;
    }
    /// <summary>Gets the shared wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; private set;
    }
    /// <summary>Gets the accepted share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get; private set;
    }
    /// <summary>Gets the accepted secret fingerprint.</summary>
    public byte[] ShareSecretHash { get; private set; } = [];
    /// <inheritdoc />
    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <inheritdoc />
    public DateTime? UpdatedAt
    {
        get; private set;
    }
}
