using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a manual gift wish creation request.
/// </summary>
/// <param name="name">The requested name.</param>
/// <param name="note">The optional owner note.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
/// <param name="quantity">The optional total desired quantity.</param>
[ExcludeFromCodeCoverage]
public class CreateWishRequest(
    string? name,
    string? note,
    string? url,
    decimal? price,
    int? quantity = null)
{
    /// <summary>
    /// Gets the requested name.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Gets the optional owner note.
    /// </summary>
    public string? Note { get; } = note;

    /// <summary>
    /// Gets the optional product URL.
    /// </summary>
    public string? Url { get; } = url;

    /// <summary>
    /// Gets the optional price in euros.
    /// </summary>
    public decimal? Price { get; } = price;

    /// <summary>
    /// Gets the optional total desired quantity, which defaults to one.
    /// </summary>
    public int? Quantity { get; } = quantity;
}
