using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the private details of a gift wish.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishDetails(
    Guid id,
    Guid wishlistId,
    string name,
    string? note,
    string? url,
    decimal? price,
    long position,
    DateTime createdAt,
    DateTime? updatedAt,
    uint version)
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
    /// Gets the optimistic concurrency version.
    /// </summary>
    public uint Version { get; } = version;
}
