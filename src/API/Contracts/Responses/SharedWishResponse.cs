using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents a gift wish exposed through a share link.
/// </summary>
[ExcludeFromCodeCoverage]
public class SharedWishResponse
{
    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the gift-wish name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the optional product URL.</summary>
    public string? Url
    {
        get; init;
    }
    /// <summary>Gets the optional price in euros.</summary>
    public decimal? Price
    {
        get; init;
    }
    /// <summary>Gets the total desired quantity.</summary>
    public int Quantity
    {
        get; init;
    }
    /// <summary>Gets the total quantity reserved by all participants.</summary>
    public int ReservedQuantity
    {
        get; init;
    }
    /// <summary>Gets the remaining quantity, clamped to zero.</summary>
    public int AvailableQuantity
    {
        get; init;
    }
    /// <summary>Gets the quantity reserved by the current participant when one is joined.</summary>
    public int? CurrentParticipantReservedQuantity
    {
        get; init;
    }

    /// <summary>Gets the optional short-lived absolute image URL.</summary>
    public string? ImageUrl
    {
        get; init;
    }
}
