using JennGllg.Fr.MonKado.Back.Domain.Enums;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Represents a wishlist.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishList
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier for the administrator.
    /// </summary>
    public Guid AdminId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier for the guest.
    /// </summary>
    public Guid GuestId { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the wishlist was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the wishlist was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether fulfilled wishes should be displayed.
    /// </summary>
    public bool ShowFulfilledWishes { get; set; }

    /// <summary>
    /// Gets or sets the type of event.
    /// </summary>
    public EventType Event { get; set; }

    /// <summary>
    /// Gets or sets the list of wishes.
    /// </summary>
    public IEnumerable<Wish> Wishes { get; set; }
}
