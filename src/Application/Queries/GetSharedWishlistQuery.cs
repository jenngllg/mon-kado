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
/// Represents public wishlist retrieval through a bearer share link.
/// </summary>
public class GetSharedWishlistQuery : IRequest<SharedWishlistResult>, IGenericValidationFailure
{
    /// <summary>Initializes a public shared-wishlist query.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The nullable bearer secret received from the client.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    /// <param name="availableOnly">Whether to return only gifts available to the current participant.</param>
    public GetSharedWishlistQuery(
        Guid shareLinkId,
        string? secret,
        Guid? memberId,
        string? guestToken,
        bool availableOnly)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
        MemberId = memberId;
        GuestToken = guestToken;
        AvailableOnly = availableOnly;
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

    /// <summary>Gets whether only gifts available to the current participant are returned.</summary>
    public bool AvailableOnly
    {
        get;
    }

    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new SharedWishlistNotFoundException();
    }
}

/// <summary>
/// Handles public wishlist retrieval through a bearer share link.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="participantService">The wishlist participant service.</param>
/// <param name="reservationService">The gift reservation service.</param>
/// <param name="logger">The logger.</param>
public class GetSharedWishlistQueryHandler(
    IWishlistShareService shareService,
    IWishlistParticipantService participantService,
    IGiftReservationService reservationService,
    ILogger<GetSharedWishlistQueryHandler> logger)
    : IRequestHandler<GetSharedWishlistQuery, SharedWishlistResult>
{
    /// <summary>Gets the publicly shared wishlist.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public wishlist content.</returns>
    /// <exception cref="SharedWishlistNotFoundException">The share link is invalid or unavailable.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<SharedWishlistResult> Handle(
        GetSharedWishlistQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.SharedWishlistRetrievalStarted(
            logger,
            request.ShareLinkId);
        var wishlist = await shareService.GetSharedAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        var participantLookup = await participantService.GetCurrentAsync(
            wishlist.Id,
            request.MemberId,
            request.GuestToken,
            cancellationToken);

        if (participantLookup.Outcome is WishlistParticipantLookupOutcome.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        WishlistParticipantDetails? currentParticipant = null;
        IReadOnlyDictionary<Guid, int>? currentQuantities = null;

        if (participantLookup.Outcome is WishlistParticipantLookupOutcome.Found &&
            participantLookup.Participant is WishlistParticipantDetails participant)
        {
            currentParticipant = participant;
            currentQuantities = await reservationService.GetQuantitiesAsync(
                wishlist.Id,
                participant.Id,
                cancellationToken);
        }

        var enrichedWishlist = CreateWishlistDetails(
            wishlist,
            currentQuantities,
            request.AvailableOnly);

        ApplicationLogMessages.SharedWishlistRetrieved(
            logger,
            request.ShareLinkId,
            wishlist.Id);

        return new SharedWishlistResult(
            enrichedWishlist,
            currentParticipant);
    }

    private static SharedWishlistDetails CreateWishlistDetails(
        SharedWishlistDetails wishlist,
        IReadOnlyDictionary<Guid, int>? currentQuantities,
        bool availableOnly)
    {
        var wishes = wishlist.Wishes
            .Select(wish => new SharedWishDetails(
                wish.Id,
                wish.Name,
                wish.Url,
                wish.Price,
                wish.Quantity,
                wish.ReservedQuantity,
                currentQuantities?.GetValueOrDefault(wish.Id)))
            .Where(wish =>
                !availableOnly ||
                wish.ReservedQuantity < wish.Quantity ||
                wish.CurrentParticipantReservedQuantity > 0)
            .ToArray();

        return new SharedWishlistDetails(
            wishlist.Id,
            wishlist.OwnerDisplayName,
            wishlist.Name,
            wishlist.Occasion,
            wishlist.EventDate,
            wishlist.Message,
            wishes);
    }
}
