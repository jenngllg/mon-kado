using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents one reservation lifecycle from the current member's history.
/// </summary>
/// <param name="id">The reservation lifecycle identifier.</param>
/// <param name="wishlistId">The original wishlist identifier.</param>
/// <param name="wishlistName">The current or retained wishlist name.</param>
/// <param name="wishId">The original gift-wish identifier.</param>
/// <param name="wishName">The current or retained gift-wish name.</param>
/// <param name="shareLinkId">The current share-link identifier when available.</param>
/// <param name="quantity">The latest reserved quantity.</param>
/// <param name="status">The lifecycle status.</param>
/// <param name="createdAt">The UTC lifecycle creation date and time.</param>
/// <param name="lastActivityAt">The UTC date and time of the latest lifecycle activity.</param>
/// <param name="endedAt">The optional UTC lifecycle end date and time.</param>
[ExcludeFromCodeCoverage]
[method: SuppressMessage(
    "CodeQuality",
    "S107:Methods should not have too many parameters",
    Justification = "The constructor represents the complete immutable reservation history response contract.")]
public class GiftReservationHistoryResponse(
    Guid id,
    Guid wishlistId,
    string wishlistName,
    Guid wishId,
    string wishName,
    Guid? shareLinkId,
    int quantity,
    GiftReservationHistoryStatus status,
    DateTime createdAt,
    DateTime lastActivityAt,
    DateTime? endedAt)
{
    /// <summary>Gets the reservation lifecycle identifier.</summary>
    public Guid Id { get; } = id;

    /// <summary>Gets the original wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>Gets the current or retained wishlist name.</summary>
    public string WishlistName { get; } = wishlistName;

    /// <summary>Gets the original gift-wish identifier.</summary>
    public Guid WishId { get; } = wishId;

    /// <summary>Gets the current or retained gift-wish name.</summary>
    public string WishName { get; } = wishName;

    /// <summary>Gets the current share-link identifier when available.</summary>
    public Guid? ShareLinkId { get; } = shareLinkId;

    /// <summary>Gets the latest reserved quantity.</summary>
    public int Quantity { get; } = quantity;

    /// <summary>Gets the lifecycle status.</summary>
    public GiftReservationHistoryStatus Status { get; } = status;

    /// <summary>Gets the UTC lifecycle creation date and time.</summary>
    public DateTime CreatedAt { get; } = createdAt;

    /// <summary>Gets the UTC date and time of the latest lifecycle activity.</summary>
    public DateTime LastActivityAt { get; } = lastActivityAt;

    /// <summary>Gets the optional UTC lifecycle end date and time.</summary>
    public DateTime? EndedAt { get; } = endedAt;
}
