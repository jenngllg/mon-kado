using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a private wishlist update request.
/// </summary>
/// <param name="name">The requested name.</param>
/// <param name="occasion">The requested occasion.</param>
/// <param name="eventDate">The optional event date.</param>
/// <param name="message">The optional owner message.</param>
[ExcludeFromCodeCoverage]
public class UpdateWishlistRequest(
    string? name,
    WishlistOccasion? occasion,
    DateOnly? eventDate,
    string? message)
{
    /// <summary>
    /// Gets the requested name.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Gets the requested occasion.
    /// </summary>
    public WishlistOccasion? Occasion { get; } = occasion;

    /// <summary>
    /// Gets the optional event date.
    /// </summary>
    public DateOnly? EventDate { get; } = eventDate;

    /// <summary>
    /// Gets the optional owner message.
    /// </summary>
    public string? Message { get; } = message;
}
