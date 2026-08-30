using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>
/// Represents retrieval of the current participant's reservation for one gift.
/// </summary>
public class GetCurrentGiftReservationQuery : IRequest<GiftReservationDetails>, IGenericValidationFailure
{
    /// <summary>Initializes a current gift-reservation query.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented share-link secret.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    public GetCurrentGiftReservationQuery(
        Guid shareLinkId,
        string? secret,
        Guid wishId,
        Guid? memberId,
        string? guestToken)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
        WishId = wishId;
        MemberId = memberId;
        GuestToken = guestToken;
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

    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        if (ShareLinkId == Guid.Empty || string.IsNullOrWhiteSpace(Secret))
            return new SharedWishlistNotFoundException();

        if (WishId == Guid.Empty)
            return new GiftReservationNotFoundException();

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new GuestSessionInvalidException();
    }
}

/// <summary>
/// Handles retrieval of the current participant's reservation for one gift.
/// </summary>
/// <param name="shareService">The wishlist share service.</param>
/// <param name="participantService">The wishlist participant service.</param>
/// <param name="reservationService">The gift reservation service.</param>
/// <param name="logger">The logger.</param>
public class GetCurrentGiftReservationQueryHandler(
    IWishlistShareService shareService,
    IWishlistParticipantService participantService,
    IGiftReservationService reservationService,
    ILogger<GetCurrentGiftReservationQueryHandler> logger)
    : IRequestHandler<GetCurrentGiftReservationQuery, GiftReservationDetails>
{
    /// <summary>Gets the current participant's reservation for one gift.</summary>
    /// <param name="request">The reservation query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current reservation.</returns>
    public async Task<GiftReservationDetails> Handle(
        GetCurrentGiftReservationQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.GiftReservationRetrievalStarted(
            logger,
            request.ShareLinkId,
            request.WishId);
        var wishlist = await shareService.GetSharedAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        var lookup = await participantService.GetCurrentAsync(
            wishlist.Id,
            request.MemberId,
            request.GuestToken,
            cancellationToken);
        var participant = ResolveParticipant(lookup);
        var reservation = await reservationService.GetAsync(
            wishlist.Id,
            request.WishId,
            participant.Id,
            cancellationToken) ?? throw new GiftReservationNotFoundException();
        ApplicationLogMessages.GiftReservationRetrieved(
            logger,
            wishlist.Id,
            request.WishId,
            reservation.Id);

        return reservation;
    }

    private static WishlistParticipantDetails ResolveParticipant(
        WishlistParticipantLookupResult lookup)
    {
        if (lookup.Outcome is WishlistParticipantLookupOutcome.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (lookup.Outcome is WishlistParticipantLookupOutcome.MissingIdentity or
            WishlistParticipantLookupOutcome.InvalidGuestSession)
        {
            throw new GuestSessionInvalidException();
        }

        if (lookup.Outcome is WishlistParticipantLookupOutcome.NotJoined)
            throw new WishlistParticipantNotFoundException();

        return lookup.Participant ?? throw new WishlistParticipantNotFoundException();
    }
}
