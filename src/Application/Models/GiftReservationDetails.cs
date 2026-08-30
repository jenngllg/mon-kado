using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the current participant's reservation for one gift.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftReservationDetails
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

    /// <summary>Gets the optimistic concurrency version.</summary>
    public uint Version
    {
        get; init;
    }
}
