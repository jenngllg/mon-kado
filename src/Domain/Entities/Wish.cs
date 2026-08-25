using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents a gift wish added to a wishlist.
/// </summary>
public class Wish : IAuditableEntity
{
    private Wish()
    {
    }

    /// <summary>
    /// Initializes a new gift wish.
    /// </summary>
    /// <param name="id">The wish identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="name">The display name.</param>
    /// <param name="note">The optional owner note.</param>
    /// <param name="url">The optional product URL.</param>
    /// <param name="price">The optional price in euros.</param>
    /// <param name="position">The allocated position inside the parent wishlist.</param>
    public Wish(
        Guid id,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
        long position)
    {
        Id = id;
        WishlistId = wishlistId;
        Name = name;
        Note = note;
        Url = url;
        Price = price;
        Position = position;
    }

    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional owner note.
    /// </summary>
    public string? Note
    {
        get; private set;
    }

    /// <summary>
    /// Gets the optional product URL.
    /// </summary>
    public string? Url
    {
        get; private set;
    }

    /// <summary>
    /// Gets the optional price in euros.
    /// </summary>
    public decimal? Price
    {
        get; private set;
    }

    /// <summary>
    /// Gets the stable position inside the parent wishlist.
    /// </summary>
    public long Position
    {
        get; private set;
    }

    /// <summary>
    /// Gets the UTC date and time when the wish was created.
    /// </summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the UTC date and time when the wish was last updated.
    /// </summary>
    public DateTime? UpdatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the PostgreSQL optimistic concurrency version.
    /// </summary>
    public uint Version
    {
        get; private set;
    }
}
