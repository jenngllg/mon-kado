using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents one gift wish inside a versioned collection response.
/// </summary>
/// <param name="wish">The private gift wish details.</param>
/// <param name="entityTag">The individual strong entity tag.</param>
[ExcludeFromCodeCoverage]
public class WishCollectionItemResponse(
    WishResponse wish,
    string entityTag)
{
    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid Id { get; } = wish.Id;

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wish.WishlistId;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; } = wish.Name;

    /// <summary>
    /// Gets the optional owner note.
    /// </summary>
    public string? Note { get; } = wish.Note;

    /// <summary>
    /// Gets the optional product URL.
    /// </summary>
    public string? Url { get; } = wish.Url;

    /// <summary>
    /// Gets the optional price in euros.
    /// </summary>
    public decimal? Price { get; } = wish.Price;

    /// <summary>
    /// Gets the position inside the parent wishlist.
    /// </summary>
    public long Position { get; } = wish.Position;

    /// <summary>
    /// Gets the UTC creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; } = wish.CreatedAt;

    /// <summary>
    /// Gets the optional UTC update date and time.
    /// </summary>
    public DateTime? UpdatedAt { get; } = wish.UpdatedAt;

    /// <summary>
    /// Gets the individual strong entity tag.
    /// </summary>
    public string EntityTag { get; } = entityTag;
}
