using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the private details of a wishlist.
/// </summary>
/// <param name="id">The wishlist identifier.</param>
/// <param name="name">The display name.</param>
/// <param name="occasion">The associated occasion.</param>
/// <param name="eventDate">The optional event date.</param>
/// <param name="message">The optional owner message.</param>
/// <param name="createdAt">The UTC creation date and time.</param>
/// <param name="updatedAt">The optional UTC update date and time.</param>
[ExcludeFromCodeCoverage]
public class WishlistResponse(
    Guid id,
    string name,
    WishlistOccasion occasion,
    DateOnly? eventDate,
    string? message,
    DateTime createdAt,
    DateTime? updatedAt)
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
}
