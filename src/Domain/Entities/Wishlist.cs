using JennGllg.Fr.MonKado.Back.Domain.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents a private wishlist owned by a member.
/// </summary>
public class Wishlist : IAuditableEntity
{
    private Wishlist()
    {
    }

    /// <summary>
    /// Initializes a new wishlist.
    /// </summary>
    /// <param name="id">The wishlist identifier.</param>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="name">The display name.</param>
    /// <param name="normalizedName">The normalized name used for uniqueness.</param>
    /// <param name="occasion">The associated occasion.</param>
    /// <param name="eventDate">The optional event date.</param>
    /// <param name="message">The optional owner message.</param>
    public Wishlist(
        Guid id,
        Guid ownerId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        NormalizedName = normalizedName;
        Occasion = occasion;
        EventDate = eventDate;
        Message = message;
    }

    /// <summary>
    /// Replaces the editable wishlist metadata.
    /// </summary>
    /// <param name="name">The normalized display name.</param>
    /// <param name="normalizedName">The normalized uniqueness key.</param>
    /// <param name="occasion">The associated occasion.</param>
    /// <param name="eventDate">The optional event date.</param>
    /// <param name="message">The optional owner message.</param>
    /// <returns><see langword="true" /> when at least one value changed; otherwise, <see langword="false" />.</returns>
    public bool Update(
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message)
    {

        if (string.Equals(
                Name,
                name,
                StringComparison.Ordinal) &&
            string.Equals(
                NormalizedName,
                normalizedName,
                StringComparison.Ordinal) &&
            Occasion == occasion &&
            Nullable.Equals(
                EventDate,
                eventDate) &&
            string.Equals(
                Message,
                message,
                StringComparison.Ordinal))
        {
            return false;
        }

        Name = name;
        NormalizedName = normalizedName;
        Occasion = occasion;
        EventDate = eventDate;
        Message = message;

        return true;
    }

    /// <summary>
    /// Gets the wishlist identifier.
    /// </summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>
    /// Gets the owner member identifier.
    /// </summary>
    public Guid OwnerId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the normalized name used for owner-scoped uniqueness.
    /// </summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the associated occasion.
    /// </summary>
    public WishlistOccasion Occasion
    {
        get; private set;
    }

    /// <summary>
    /// Gets the optional event date.
    /// </summary>
    public DateOnly? EventDate
    {
        get; private set;
    }

    /// <summary>
    /// Gets the optional owner message.
    /// </summary>
    public string? Message
    {
        get; private set;
    }

    /// <summary>
    /// Gets the UTC date and time when the wishlist was created.
    /// </summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the UTC date and time when the wishlist was last updated.
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
