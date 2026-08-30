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
/// Represents cancellation of the current participant's reservation.
/// </summary>
public class CancelGiftReservationCommand : IRequest, IGenericValidationFailure
{
    /// <summary>Initializes a gift-reservation cancellation.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented share-link secret.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    /// <param name="expectedVersion">The version supplied by the client.</param>
    public CancelGiftReservationCommand(
        Guid shareLinkId,
        string? secret,
        Guid wishId,
        Guid? memberId,
        string? guestToken,
        uint expectedVersion)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
        WishId = wishId;
        MemberId = memberId;
        GuestToken = guestToken;
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

    /// <summary>Gets the expected reservation version.</summary>
    public uint ExpectedVersion
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
/// Handles gift-reservation cancellation requests.
/// </summary>
/// <param name="shareService">The wishlist share service.</param>
/// <param name="reservationService">The gift reservation service.</param>
/// <param name="logger">The logger.</param>
public class CancelGiftReservationCommandHandler(
    IWishlistShareService shareService,
    IGiftReservationService reservationService,
    ILogger<CancelGiftReservationCommandHandler> logger)
    : IRequestHandler<CancelGiftReservationCommand>
{
    /// <summary>Cancels the current participant's reservation.</summary>
    /// <param name="request">The cancellation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        CancelGiftReservationCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.GiftReservationCancellationStarted(
            logger,
            request.ShareLinkId,
            request.WishId);
        var wishlist = await shareService.GetSharedAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        var cancelled = await reservationService.CancelAsync(
            new GiftReservationCancellationRequest
            {
                ShareLinkId = request.ShareLinkId,
                ShareSecret = request.Secret ?? string.Empty,
                WishlistId = wishlist.Id,
                WishId = request.WishId,
                MemberId = request.MemberId,
                GuestToken = request.GuestToken,
                ExpectedVersion = request.ExpectedVersion
            },
            cancellationToken);

        if (!cancelled)
            throw new GiftReservationNotFoundException();

        ApplicationLogMessages.GiftReservationCancelled(
            logger,
            wishlist.Id,
            request.WishId);
    }
}
