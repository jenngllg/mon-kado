using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Gets only the owner's personal reservation without shared access.</summary>
/// <param name="ownerId">The authenticated owner.</param>
/// <param name="wishlistId">The private parent.</param>
/// <param name="wishId">The private wish.</param>
public class GetOwnedGiftReservationQuery(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId) : IRequest<GiftReservationDetails>
{
    /// <summary>Gets the authenticated owner.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the private parent.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the private wish.</summary>
    public Guid WishId { get; } = wishId;

}

/// <summary>Handles the validated private-owner request.</summary>
/// <param name="reservationService">The common reservation engine.</param>
/// <param name="logger">The structured logger.</param>
public class GetOwnedGiftReservationQueryHandler(
    IGiftReservationService reservationService,
    ILogger<GetOwnedGiftReservationQueryHandler> logger) : IRequestHandler<GetOwnedGiftReservationQuery, GiftReservationDetails>
{
    /// <summary>Gets only the owner's personal reservation without shared access.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The confirmed reservation result.</returns>
    public async Task<GiftReservationDetails> Handle(
        GetOwnedGiftReservationQuery request,
        CancellationToken cancellationToken)
    {
        var reservation = await reservationService.GetOwnedAsync(
            request.OwnerId,
            request.WishlistId,
            request.WishId,
            cancellationToken) ?? throw new GiftReservationNotFoundException();
        ApplicationLogMessages.GiftReservationRetrieved(
            logger,
            request.WishlistId,
            request.WishId,
            reservation.Id);

        return reservation;
    }
}
