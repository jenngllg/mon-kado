using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents creation or replacement of the current participant's reservation.
/// </summary>
public class UpsertGiftReservationCommand : IRequest<GiftReservationMutationResult>, IGenericValidationFailure
{
    /// <summary>Initializes a gift-reservation mutation.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented share-link secret.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    /// <param name="quantity">The requested absolute quantity.</param>
    /// <param name="expectedVersion">The optional version supplied by the client.</param>
    public UpsertGiftReservationCommand(
        Guid shareLinkId,
        string? secret,
        Guid wishId,
        Guid? memberId,
        string? guestToken,
        int? quantity,
        uint? expectedVersion)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
        WishId = wishId;
        MemberId = memberId;
        GuestToken = guestToken;
        Quantity = quantity;
        ExpectedVersion = expectedVersion;
    }

    /// <summary>Gets the share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get;
    }

    /// <summary>Gets the presented share-link secret.</summary>
    public string? Secret
    {
        get;
    }

    /// <summary>Gets the gift-wish identifier.</summary>
    public Guid WishId
    {
        get;
    }

    /// <summary>Gets the optional authenticated member identifier.</summary>
    public Guid? MemberId
    {
        get;
    }

    /// <summary>Gets the optional browser guest token.</summary>
    public string? GuestToken
    {
        get;
    }

    /// <summary>Gets the requested absolute quantity.</summary>
    public int? Quantity
    {
        get;
    }

    /// <summary>Gets the optional version supplied by the client.</summary>
    public uint? ExpectedVersion
    {
        get;
    }

    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (ShareLinkId == Guid.Empty || string.IsNullOrWhiteSpace(Secret))
            return new SharedWishlistNotFoundException();

        if (WishId == Guid.Empty)
            return new WishNotFoundException();

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles gift-reservation creation and replacement.
/// </summary>
/// <param name="shareService">The wishlist share service.</param>
/// <param name="reservationService">The gift reservation service.</param>
/// <param name="logger">The logger.</param>
public class UpsertGiftReservationCommandHandler(
    IWishlistShareService shareService,
    IGiftReservationService reservationService,
    ILogger<UpsertGiftReservationCommandHandler> logger)
    : IRequestHandler<UpsertGiftReservationCommand, GiftReservationMutationResult>
{
    /// <summary>Creates or replaces the current participant's reservation.</summary>
    /// <param name="request">The reservation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current reservation and whether it was created.</returns>
    public async Task<GiftReservationMutationResult> Handle(
        UpsertGiftReservationCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.GiftReservationMutationStarted(
            logger,
            request.ShareLinkId,
            request.WishId);
        var wishlist = await shareService.GetSharedAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        var result = await reservationService.UpsertAsync(
            new GiftReservationMutationRequest
            {
                ReservationId = Guid.CreateVersion7(),
                ShareLinkId = request.ShareLinkId,
                ShareSecret = request.Secret ?? string.Empty,
                WishlistId = wishlist.Id,
                WishId = request.WishId,
                MemberId = request.MemberId,
                GuestToken = request.GuestToken,
                Quantity = request.Quantity ?? 0,
                ExpectedVersion = request.ExpectedVersion
            },
            cancellationToken);
        ApplicationLogMessages.GiftReservationMutated(
            logger,
            wishlist.Id,
            request.WishId,
            result.Reservation.Id);

        return result;
    }
}
