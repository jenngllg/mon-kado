using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the private details of a wishlist.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishlistDetails(
    Guid id,
    string name,
    WishlistOccasion occasion,
    DateOnly? eventDate,
    string? message,
    DateTime createdAt,
    DateTime? updatedAt,
    uint version)
{
    /// <summary>
    /// Gets the wishlist identifier.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the associated occasion.
    /// </summary>
    public WishlistOccasion Occasion { get; } = occasion;

    /// <summary>
    /// Gets the optional event date.
    /// </summary>
    public DateOnly? EventDate { get; } = eventDate;

    /// <summary>
    /// Gets the optional owner message.
    /// </summary>
    public string? Message { get; } = message;

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
