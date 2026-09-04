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
/// Represents detailed public gift-wish retrieval through a bearer share link.
/// </summary>
public class GetSharedWishQuery : IRequest<SharedWishDetail>, IGenericValidationFailure
{
    /// <summary>Initializes a detailed public gift-wish query.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The nullable bearer secret received from the client.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    public GetSharedWishQuery(
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

    /// <summary>Gets the nullable bearer secret received from the client.</summary>
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
        if (ShareLinkId == Guid.Empty)
            return new RequestValidationException(validationErrors);

        if (WishId == Guid.Empty)
            return new RequestValidationException(validationErrors);

        if (string.IsNullOrWhiteSpace(Secret))
            return new SharedWishlistNotFoundException();

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles detailed public gift-wish retrieval through a bearer share link.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="participantService">The wishlist participant service.</param>
/// <param name="reservationService">The gift reservation service.</param>
/// <param name="logger">The logger.</param>
public class GetSharedWishQueryHandler(
    IWishlistShareService shareService,
    IWishlistParticipantService participantService,
    IGiftReservationService reservationService,
    ILogger<GetSharedWishQueryHandler> logger)
    : IRequestHandler<GetSharedWishQuery, SharedWishDetail>
{
    /// <summary>Gets detailed information about one publicly shared gift wish.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detailed public gift-wish information.</returns>
    /// <exception cref="SharedWishlistNotFoundException">The share link is invalid or unavailable.</exception>
    /// <exception cref="SharedWishNotFoundException">The gift wish is unavailable under the shared wishlist.</exception>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<SharedWishDetail> Handle(
        GetSharedWishQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.SharedWishRetrievalStarted(
            logger,
            request.ShareLinkId,
            request.WishId);
        var lookup = await shareService.GetSharedWishAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            request.WishId,
            cancellationToken);

        if (lookup.Outcome is SharedWishLookupOutcome.SharedWishlistNotFound)
            throw new SharedWishlistNotFoundException();

        if (lookup.Outcome is not SharedWishLookupOutcome.Found ||
            lookup.WishlistId is not Guid wishlistId ||
            lookup.Wish is not SharedWishDetail wish)
        {
            throw new SharedWishNotFoundException();
        }

        var participantLookup = await participantService.GetCurrentAsync(
            wishlistId,
            request.MemberId,
            request.GuestToken,
            cancellationToken);

        if (participantLookup.Outcome is WishlistParticipantLookupOutcome.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        int? currentQuantity = null;

        if (participantLookup.Outcome is WishlistParticipantLookupOutcome.Found &&
            participantLookup.Participant is WishlistParticipantDetails participant)
        {
            var reservation = await reservationService.GetAsync(
                wishlistId,
                wish.Id,
                participant.Id,
                cancellationToken);
            currentQuantity = reservation?.Quantity ?? 0;
        }

        var result = new SharedWishDetail(
            wish.Id,
            wish.Name,
            wish.Note,
            wish.Url,
            wish.Price,
            wish.Quantity,
            wish.ReservedQuantity,
            currentQuantity);

        ApplicationLogMessages.SharedWishRetrieved(
            logger,
            request.ShareLinkId,
            wishlistId,
            wish.Id);

        return result;
    }
}
