using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Carries a validated reservation replacement to the persistence service.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftReservationMutationRequest
{
    /// <summary>Gets the generated reservation identifier.</summary>
    public Guid ReservationId
    {
        get; init;
    }

    /// <summary>Gets the share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get; init;
    }

    /// <summary>Gets the presented share-link secret.</summary>
    public string ShareSecret { get; init; } = string.Empty;

    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }

    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid WishId
    {
        get; init;
    }

    /// <summary>Gets the optional authenticated member identifier.</summary>
    public Guid? MemberId
    {
        get; init;
    }

    /// <summary>Gets the optional browser guest token.</summary>
    public string? GuestToken
    {
        get; init;
    }

    /// <summary>Gets the requested absolute quantity.</summary>
    public int Quantity
    {
        get; init;
    }

    /// <summary>Gets the optional version supplied by the client.</summary>
    public uint? ExpectedVersion
    {
        get; init;
    }
}
