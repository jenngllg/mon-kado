using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents one member or guest participating in a wishlist.
/// </summary>
public class WishlistParticipant : IAuditableEntity
{
    private WishlistParticipant()
    {
    }

    /// <summary>
    /// Initializes a guest wishlist participant.
    /// </summary>
    /// <param name="id">The participant identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="guestSessionId">The guest session identifier.</param>
    /// <param name="guestDisplayName">The guest display name.</param>
    public WishlistParticipant(
        Guid id,
        Guid wishlistId,
        Guid guestSessionId,
        string guestDisplayName)
    {
        Id = id;
        WishlistId = wishlistId;
        GuestSessionId = guestSessionId;
        GuestDisplayName = guestDisplayName;
    }

    /// <summary>
    /// Creates a member wishlist participant.
    /// </summary>
    /// <param name="id">The participant identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <returns>The member participant.</returns>
    public static WishlistParticipant CreateMember(
        Guid id,
        Guid wishlistId,
        Guid memberId)
    {
        return new WishlistParticipant
        {
            Id = id,
            WishlistId = wishlistId,
            MemberId = memberId
        };
    }

    /// <summary>
    /// Attaches an existing guest participation to a member.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    public void AttachToMember(Guid memberId)
    {
        MemberId = memberId;
        GuestSessionId = null;
    }

    /// <summary>Gets the participant identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>Gets the optional member identifier.</summary>
    public Guid? MemberId
    {
        get; private set;
    }

    /// <summary>Gets the optional guest session identifier.</summary>
    public Guid? GuestSessionId
    {
        get; private set;
    }

    /// <summary>Gets the retained guest display name, or an empty value for member-created participants.</summary>
    public string GuestDisplayName
    {
        get; private set;
    } = string.Empty;

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter through change tracking.")]
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter through change tracking.")]
    public DateTime? UpdatedAt
    {
        get; private set;
    }
}
