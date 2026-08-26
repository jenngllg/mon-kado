using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the public content exposed by a wishlist share link.
/// </summary>
/// <param name="id">The wishlist identifier.</param>
/// <param name="ownerDisplayName">The public owner display name.</param>
/// <param name="name">The wishlist name.</param>
/// <param name="occasion">The wishlist occasion.</param>
/// <param name="eventDate">The optional event date.</param>
/// <param name="message">The optional owner message.</param>
/// <param name="wishes">The ordered public gift wishes.</param>
[ExcludeFromCodeCoverage]
public class SharedWishlistDetails(
    Guid id,
    string ownerDisplayName,
    string name,
    WishlistOccasion occasion,
    DateOnly? eventDate,
    string? message,
    IReadOnlyCollection<SharedWishDetails> wishes)
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the public owner display name.</summary>
    public string OwnerDisplayName { get; } = ownerDisplayName;
    /// <summary>Gets the wishlist name.</summary>
    public string Name { get; } = name;
    /// <summary>Gets the wishlist occasion.</summary>
    public WishlistOccasion Occasion { get; } = occasion;
    /// <summary>Gets the optional event date.</summary>
    public DateOnly? EventDate { get; } = eventDate;
    /// <summary>Gets the optional owner message.</summary>
    public string? Message { get; } = message;
    /// <summary>Gets the ordered public gift wishes.</summary>
    public IReadOnlyCollection<SharedWishDetails> Wishes { get; } = wishes;
}
