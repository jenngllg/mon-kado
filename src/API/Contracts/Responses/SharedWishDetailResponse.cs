using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents detailed public information about a shared gift wish.
/// </summary>
[ExcludeFromCodeCoverage]
public class SharedWishDetailResponse
{
    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>Gets the gift-wish name.</summary>
    public string Name
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the optional public description written by the owner.</summary>
    public string? Note
    {
        get; init;
    }

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

    /// <summary>Gets the remaining quantity.</summary>
    public int AvailableQuantity
    {
        get; init;
    }

    /// <summary>Gets the quantity reserved by the current participant when one is joined.</summary>
    public int? CurrentParticipantReservedQuantity
    {
        get; init;
    }
}
