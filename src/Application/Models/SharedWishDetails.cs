using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents public gift-wish details.
/// </summary>
/// <param name="id">The gift-wish identifier.</param>
/// <param name="name">The gift-wish name.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
[ExcludeFromCodeCoverage]
public class SharedWishDetails(
    Guid id,
    string name,
    string? url,
    decimal? price)
{
    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid Id { get; } = id;
    /// <summary>Gets the gift-wish name.</summary>
    public string Name { get; } = name;
    /// <summary>Gets the optional product URL.</summary>
    public string? Url { get; } = url;
    /// <summary>Gets the optional price in euros.</summary>
    public decimal? Price { get; } = price;
}
