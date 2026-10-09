using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Creates or replaces an owner's personal reservation.</summary>
/// <param name="ownerId">The authenticated owner.</param>
/// <param name="wishlistId">The private parent.</param>
/// <param name="wishId">The private wish.</param>
/// <param name="quantity">The nullable quantity.</param>
/// <param name="expectedVersion">The optional current version.</param>
public class UpsertOwnedGiftReservationCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId,
    int? quantity,
    uint? expectedVersion) : IRequest<GiftReservationMutationResult>
{
    /// <summary>Gets the authenticated owner.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the private parent.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the private wish.</summary>
    public Guid WishId { get; } = wishId;
    /// <summary>Gets the absolute quantity.</summary>
    public int? Quantity { get; } = quantity;
    /// <summary>Gets the optional current version.</summary>
    public uint? ExpectedVersion { get; } = expectedVersion;
}

/// <summary>Handles the validated private-owner request.</summary>
/// <param name="reservationService">The common reservation engine.</param>
/// <param name="logger">The structured logger.</param>
public class UpsertOwnedGiftReservationCommandHandler(
    IGiftReservationService reservationService,
    ILogger<UpsertOwnedGiftReservationCommandHandler> logger) : IRequestHandler<UpsertOwnedGiftReservationCommand, GiftReservationMutationResult>
{
    /// <summary>Creates or replaces an owner's personal reservation.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The confirmed reservation result.</returns>
    public async Task<GiftReservationMutationResult> Handle(
        UpsertOwnedGiftReservationCommand request,
        CancellationToken cancellationToken)
    {
        var result = await reservationService.UpsertAsync(
            new GiftReservationMutationRequest
            {
                ReservationId = Guid.CreateVersion7(),
                IsOwnerReservation = true,
                MemberId = request.OwnerId,
                WishlistId = request.WishlistId,
                WishId = request.WishId,
                Quantity = request.Quantity.GetValueOrDefault(),
                ExpectedVersion = request.ExpectedVersion
            },
            cancellationToken);
        ApplicationLogMessages.GiftReservationMutated(
            logger,
            request.WishlistId,
            request.WishId,
            result.Reservation.Id);

        return result;
    }
}
