using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the current participant's reservation for one gift.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftReservationResponse
{
    /// <summary>Gets the reservation identifier.</summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid WishId
    {
        get; init;
    }

    /// <summary>Gets the reserved quantity.</summary>
    public int Quantity
    {
        get; init;
    }

    /// <summary>Gets the UTC creation date and time.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }

    /// <summary>Gets the optional UTC update date and time.</summary>
    public DateTime? UpdatedAt
    {
        get; init;
    }
}
