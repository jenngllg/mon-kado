using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents one durable member reservation lifecycle.
/// </summary>
public class GiftReservationHistory
{
    private GiftReservationHistory()
    {
    }

    /// <summary>
    /// Initializes an active reservation history entry.
    /// </summary>
    /// <param name="id">The reservation lifecycle identifier.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishlistName">The retained wishlist name.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="wishName">The retained gift-wish name.</param>
    /// <param name="quantity">The reserved quantity.</param>
    /// <param name="createdAt">The UTC lifecycle creation date and time.</param>
    /// <param name="lastActivityAt">The UTC date and time of the latest lifecycle activity.</param>
    [SuppressMessage(
        "CodeQuality",
        "S107:Methods should not have too many parameters",
        Justification = "The constructor captures the complete initial state of one reservation lifecycle.")]
    public GiftReservationHistory(
        Guid id,
        Guid memberId,
        Guid wishlistId,
        string wishlistName,
        Guid wishId,
        string wishName,
        int quantity,
        DateTime createdAt,
        DateTime lastActivityAt)
    {
        Id = id;
        MemberId = memberId;
        WishlistId = wishlistId;
        WishlistName = wishlistName;
        WishId = wishId;
        WishName = wishName;
        Quantity = quantity;
        CreatedAt = createdAt;
        LastActivityAt = lastActivityAt;
    }

    /// <summary>Gets the reservation lifecycle identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the member identifier.</summary>
    public Guid MemberId
    {
        get; private set;
    }

    /// <summary>Gets the original wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>Gets the retained wishlist name.</summary>
    public string WishlistName
    {
        get; private set;
    } = string.Empty;

    /// <summary>Gets the original gift-wish identifier.</summary>
    public Guid WishId
    {
        get; private set;
    }

    /// <summary>Gets the retained gift-wish name.</summary>
    public string WishName
    {
        get; private set;
    } = string.Empty;

    /// <summary>Gets the latest quantity from this lifecycle.</summary>
    public int Quantity
    {
        get; private set;
    }

    /// <summary>Gets the lifecycle status.</summary>
    public GiftReservationHistoryStatus Status
    {
        get; private set;
    } = GiftReservationHistoryStatus.Active;

    /// <summary>Gets the UTC lifecycle creation date and time.</summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>Gets the UTC date and time of the latest lifecycle activity.</summary>
    public DateTime LastActivityAt
    {
        get; private set;
    }

    /// <summary>Gets the optional UTC lifecycle end date and time.</summary>
    public DateTime? EndedAt
    {
        get; private set;
    }

    /// <summary>
    /// Replaces the quantity of an active reservation lifecycle.
    /// </summary>
    /// <param name="quantity">The new reserved quantity.</param>
    /// <param name="activityAt">The UTC activity date and time.</param>
    /// <returns><see langword="true" /> when the quantity changed.</returns>
    public bool UpdateQuantity(
        int quantity,
        DateTime activityAt)
    {
        if (Quantity == quantity)
            return false;

        Quantity = quantity;
        LastActivityAt = activityAt;

        return true;
    }

    /// <summary>
    /// Ends an active reservation lifecycle.
    /// </summary>
    /// <param name="status">The terminal lifecycle status.</param>
    /// <param name="endedAt">The UTC lifecycle end date and time.</param>
    /// <returns><see langword="true" /> when the active lifecycle was ended.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The requested status is not terminal.</exception>
    public bool End(
        GiftReservationHistoryStatus status,
        DateTime endedAt)
    {
        if (status is GiftReservationHistoryStatus.Active)
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "An active reservation history must be ended with a terminal status.");

        if (Status is not GiftReservationHistoryStatus.Active)
            return false;

        Status = status;
        LastActivityAt = endedAt;
        EndedAt = endedAt;

        return true;
    }
}
