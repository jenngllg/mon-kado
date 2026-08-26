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
/// Represents current-participant retrieval through an active share link.
/// </summary>
public class GetCurrentWishlistParticipantQuery : IRequest<WishlistParticipantDetails>, IGenericValidationFailure
{
    /// <summary>Initializes the current-participant query.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented share-link secret.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    public GetCurrentWishlistParticipantQuery(
        Guid shareLinkId,
        string? secret,
        Guid? memberId,
        string? guestToken)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
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

    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new SharedWishlistNotFoundException();
    }
}

/// <summary>
/// Handles current-participant retrieval through an active share link.
/// </summary>
/// <param name="shareService">The wishlist share service.</param>
/// <param name="participantService">The wishlist participant service.</param>
/// <param name="logger">The logger.</param>
public class GetCurrentWishlistParticipantQueryHandler(
    IWishlistShareService shareService,
    IWishlistParticipantService participantService,
    ILogger<GetCurrentWishlistParticipantQueryHandler> logger)
    : IRequestHandler<GetCurrentWishlistParticipantQuery, WishlistParticipantDetails>
{
    /// <summary>Gets the participant associated with the current caller.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current participant.</returns>
    public async Task<WishlistParticipantDetails> Handle(
        GetCurrentWishlistParticipantQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistParticipantRetrievalStarted(
            logger,
            request.ShareLinkId);
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
        ApplicationLogMessages.WishlistParticipantRetrieved(
            logger,
            wishlist.Id,
            participant.Id);

        return participant;
    }

    private static WishlistParticipantDetails ResolveParticipant(WishlistParticipantLookupResult lookup)
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
