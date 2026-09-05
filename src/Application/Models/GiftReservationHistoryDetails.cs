using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents one member reservation lifecycle in history.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftReservationHistoryDetails
{
    /// <summary>Gets the reservation lifecycle identifier.</summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>Gets the original wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }

    /// <summary>Gets the current or retained wishlist name.</summary>
    public string WishlistName
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the original gift-wish identifier.</summary>
    public Guid WishId
    {
        get; init;
    }

    /// <summary>Gets the current or retained gift-wish name.</summary>
    public string WishName
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the current share-link identifier when the wishlist is shared.</summary>
    public Guid? ShareLinkId
    {
        get; init;
    }

    /// <summary>Gets the latest quantity from this lifecycle.</summary>
    public int Quantity
    {
        get; init;
    }

    /// <summary>Gets the lifecycle status.</summary>
    public GiftReservationHistoryStatus Status
    {
        get; init;
    }

    /// <summary>Gets the UTC lifecycle creation date and time.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }

    /// <summary>Gets the UTC date and time of the latest lifecycle activity.</summary>
    public DateTime LastActivityAt
    {
        get; init;
    }

    /// <summary>Gets the optional UTC lifecycle end date and time.</summary>
    public DateTime? EndedAt
    {
        get; init;
    }
}
