using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Cancels only the owner's personal reservation.</summary>
/// <param name="ownerId">The authenticated owner.</param>
/// <param name="wishlistId">The private parent.</param>
/// <param name="wishId">The private wish.</param>
/// <param name="expectedVersion">The required current version.</param>
public class CancelOwnedGiftReservationCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId,
    uint expectedVersion) : IRequest
{
    /// <summary>Gets the authenticated owner.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the private parent.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the private wish.</summary>
    public Guid WishId { get; } = wishId;
    /// <summary>Gets the required current version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;
}

/// <summary>Handles the validated private-owner request.</summary>
/// <param name="reservationService">The common reservation engine.</param>
/// <param name="logger">The structured logger.</param>
public class CancelOwnedGiftReservationCommandHandler(
    IGiftReservationService reservationService,
    ILogger<CancelOwnedGiftReservationCommandHandler> logger) : IRequestHandler<CancelOwnedGiftReservationCommand>
{
    /// <summary>Cancels only the owner's personal reservation.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after cancellation.</returns>
    public async Task Handle(
        CancelOwnedGiftReservationCommand request,
        CancellationToken cancellationToken)
    {
        var cancelled = await reservationService.CancelAsync(
            new GiftReservationCancellationRequest
            {
                IsOwnerReservation = true,
                MemberId = request.OwnerId,
                WishlistId = request.WishlistId,
                WishId = request.WishId,
                ExpectedVersion = request.ExpectedVersion
            },
            cancellationToken);

        if (!cancelled)
            throw new GiftReservationNotFoundException();

        ApplicationLogMessages.GiftReservationCancelled(
            logger,
            request.WishlistId,
            request.WishId);
    }
}

