using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the private details of a gift wish.
/// </summary>
/// <param name="id">The wish identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="name">The display name.</param>
/// <param name="note">The optional owner note.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
/// <param name="position">The stable position inside the parent wishlist.</param>
/// <param name="createdAt">The UTC creation date and time.</param>
/// <param name="updatedAt">The optional UTC update date and time.</param>
/// <param name="quantity">The total desired quantity.</param>
/// <param name="imageUrl">The optional short-lived absolute image URL.</param>
[ExcludeFromCodeCoverage]
public class WishResponse(
    Guid id,
    Guid wishlistId,
    string name,
    string? note,
    string? url,
    decimal? price,
    long position,
    DateTime createdAt,
    DateTime? updatedAt,
    int quantity = 1,
    string? imageUrl = null)
{
    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the optional owner note.
    /// </summary>
    public string? Note { get; } = note;

    /// <summary>
    /// Gets the optional product URL.
    /// </summary>
    public string? Url { get; } = url;

    /// <summary>
    /// Gets the optional price in euros.
    /// </summary>
    public decimal? Price { get; } = price;

    /// <summary>
    /// Gets the total desired quantity.
    /// </summary>
    public int Quantity { get; } = quantity;

    /// <summary>
    /// Gets the stable position inside the parent wishlist.
    /// </summary>
    public long Position { get; } = position;

    /// <summary>
    /// Gets the UTC creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; } = createdAt;

    /// <summary>
    /// Gets the optional UTC update date and time.
    /// </summary>
    public DateTime? UpdatedAt { get; } = updatedAt;

    /// <summary>
    /// Gets the optional short-lived absolute image URL.
    /// </summary>
    public string? ImageUrl { get; } = imageUrl;
}
