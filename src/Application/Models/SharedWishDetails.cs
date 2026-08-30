using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents public gift-wish details.
/// </summary>
/// <param name="id">The gift-wish identifier.</param>
/// <param name="name">The gift-wish name.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
/// <param name="quantity">The total desired quantity.</param>
/// <param name="reservedQuantity">The total quantity reserved by all participants.</param>
/// <param name="currentParticipantReservedQuantity">The optional current-participant quantity.</param>
[ExcludeFromCodeCoverage]
public class SharedWishDetails(
    Guid id,
    string name,
    string? url,
    decimal? price,
    int quantity = 1,
    int reservedQuantity = 0,
    int? currentParticipantReservedQuantity = null)
{
    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the gift-wish name.</summary>
    public string Name { get; } = name;
    /// <summary>Gets the optional product URL.</summary>
    public string? Url { get; } = url;
    /// <summary>Gets the optional price in euros.</summary>
    public decimal? Price { get; } = price;
    /// <summary>Gets the total desired quantity.</summary>
    public int Quantity { get; } = quantity;
    /// <summary>Gets the total quantity reserved by all participants.</summary>
    public int ReservedQuantity { get; } = reservedQuantity;
    /// <summary>Gets the quantity reserved by the current participant when one is joined.</summary>
    public int? CurrentParticipantReservedQuantity { get; } = currentParticipantReservedQuantity;
}
