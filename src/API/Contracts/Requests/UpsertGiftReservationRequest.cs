using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents an absolute gift-reservation quantity replacement.
/// </summary>
/// <param name="quantity">The requested reserved quantity.</param>
[ExcludeFromCodeCoverage]
public class UpsertGiftReservationRequest(int? quantity)
{
    /// <summary>Gets the requested reserved quantity.</summary>
    public int? Quantity { get; } = quantity;
}
