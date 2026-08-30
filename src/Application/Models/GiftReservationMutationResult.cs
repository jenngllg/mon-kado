using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the result of creating or replacing a gift reservation.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftReservationMutationResult
{
    /// <summary>Gets the current reservation.</summary>
    public GiftReservationDetails Reservation { get; init; } = new();

    /// <summary>Gets a value indicating whether the reservation was created.</summary>
    public bool IsCreated
    {
        get; init;
    }
}
