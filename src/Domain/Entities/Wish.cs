using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents a gift wish added to a wishlist.
/// </summary>
public class Wish : IAuditableEntity
{
    /// <summary>
    /// Gets the required SHA-256 image content hash length.
    /// </summary>
    public const int ImageContentHashLength = 32;

    private byte[]? _imageContentHash;

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
    /// <param name="quantity">The total desired quantity.</param>
    [SuppressMessage(
        "CodeQuality",
        "S107:Methods should not have too many parameters",
        Justification = "The constructor captures the complete initial state of a gift wish.")]
    public Wish(
        Guid id,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
        long position,
        int quantity = 1)
    {
        Id = id;
        WishlistId = wishlistId;
        Name = name;
        Note = note;
        Url = url;
        Price = price;
        Quantity = quantity;
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
    /// Gets the total desired quantity.
    /// </summary>
    public int Quantity
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
    /// Gets the optional immutable image identifier.
    /// </summary>
    public Guid? ImageId
    {
        get; private set;
    }

    /// <summary>
    /// Gets a copy of the optional normalized image content hash.
    /// </summary>
    public byte[]? ImageContentHash => _imageContentHash?.ToArray();

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

    /// <summary>
    /// Replaces the editable gift wish values.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="note">The optional owner note.</param>
    /// <param name="url">The optional product URL.</param>
    /// <param name="price">The optional price in euros.</param>
    /// <param name="quantity">The total desired quantity.</param>
    /// <returns><see langword="true" /> when at least one value changed.</returns>
    public bool Update(
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity = 1)
    {
        var hasChanged = Name != name ||
            Note != note ||
            Url != url ||
            Price != price ||
            Quantity != quantity;

        if (!hasChanged)
            return false;

        Name = name;
        Note = note;
        Url = url;
        Price = price;
        Quantity = quantity;

        return true;
    }

    /// <summary>
    /// Moves the gift wish to another positive position.
    /// </summary>
    /// <param name="position">The new position inside the parent wishlist.</param>
    /// <returns><see langword="true" /> when the position changed.</returns>
    public bool MoveTo(long position)
    {
        if (Position == position)
            return false;

        Position = position;

        return true;
    }

    /// <summary>
    /// Determines whether the current image has the supplied normalized content hash.
    /// </summary>
    /// <param name="contentHash">The SHA-256 content hash.</param>
    /// <returns><see langword="true" /> when the current image has the same content.</returns>
    public bool HasImageContentHash(ReadOnlySpan<byte> contentHash)
    {
        if (_imageContentHash is null || contentHash.Length != ImageContentHashLength)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            _imageContentHash,
            contentHash);
    }

    /// <summary>
    /// Adds or replaces the normalized image attached to this gift wish.
    /// </summary>
    /// <param name="imageId">The immutable image identifier.</param>
    /// <param name="contentHash">The SHA-256 hash of the normalized WebP content.</param>
    /// <returns>The identifier of the replaced image, when one existed.</returns>
    public Guid? ReplaceImage(
        Guid imageId,
        byte[] contentHash)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(
            imageId,
            Guid.Empty);
        ArgumentNullException.ThrowIfNull(contentHash);

        if (contentHash.Length != ImageContentHashLength)
        {
            throw new ArgumentException(
                $"The image content hash must contain exactly {ImageContentHashLength} bytes.",
                nameof(contentHash));
        }

        var replacedImageId = ImageId;
        ImageId = imageId;
        _imageContentHash = contentHash.ToArray();

        return replacedImageId;
    }

    /// <summary>Removes the image reference and its content hash.</summary>
    /// <returns>The removed image identifier, when an image existed.</returns>
    public Guid? RemoveImage()
    {
        var imageId = ImageId;
        ImageId = null;
        _imageContentHash = null;

        return imageId;
    }
}
