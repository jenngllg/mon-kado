using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents detailed public information about a shared gift wish.
/// </summary>
/// <param name="id">The gift-wish identifier.</param>
/// <param name="name">The gift-wish name.</param>
/// <param name="note">The optional public description written by the owner.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
/// <param name="quantity">The total desired quantity.</param>
/// <param name="reservedQuantity">The total quantity reserved by all participants.</param>
/// <param name="currentParticipantReservedQuantity">The optional current-participant quantity.</param>
[ExcludeFromCodeCoverage]
public class SharedWishDetail(
    Guid id,
    string name,
    string? note,
    string? url,
    decimal? price,
    int quantity,
    int reservedQuantity,
    int? currentParticipantReservedQuantity)
{
    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid Id { get; } = id;

    /// <summary>Gets the gift-wish name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the optional public description written by the owner.</summary>
    public string? Note { get; } = note;

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
