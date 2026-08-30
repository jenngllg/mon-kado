using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents the quantity of one gift reserved by one wishlist participant.
/// </summary>
public class GiftReservation : IAuditableEntity
{
    private GiftReservation()
    {
    }

    /// <summary>
    /// Initializes a gift reservation.
    /// </summary>
    /// <param name="id">The reservation identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="wishlistParticipantId">The participant identifier.</param>
    /// <param name="quantity">The reserved quantity.</param>
    public GiftReservation(
        Guid id,
        Guid wishlistId,
        Guid wishId,
        Guid wishlistParticipantId,
        int quantity)
    {
        Id = id;
        WishlistId = wishlistId;
        WishId = wishId;
        WishlistParticipantId = wishlistParticipantId;
        Quantity = quantity;
    }

    /// <summary>Gets the reservation identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid WishId
    {
        get; private set;
    }

    /// <summary>Gets the participant identifier.</summary>
    public Guid WishlistParticipantId
    {
        get; private set;
    }

    /// <summary>Gets the reserved quantity.</summary>
    public int Quantity
    {
        get; private set;
    }

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

    /// <summary>Gets the PostgreSQL optimistic concurrency version.</summary>
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter from PostgreSQL xmin.")]
    public uint Version
    {
        get; private set;
    }

    /// <summary>Replaces the absolute reserved quantity.</summary>
    /// <param name="quantity">The new reserved quantity.</param>
    /// <returns><see langword="true" /> when the quantity changed.</returns>
    public bool UpdateQuantity(int quantity)
    {
        if (Quantity == quantity)
            return false;

        Quantity = quantity;

        return true;
    }

    /// <summary>Transfers the reservation to another participant.</summary>
    /// <param name="wishlistParticipantId">The destination participant identifier.</param>
    public void TransferTo(Guid wishlistParticipantId)
    {
        WishlistParticipantId = wishlistParticipantId;
    }
}
