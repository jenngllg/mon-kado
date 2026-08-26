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
/// Represents joining a wishlist through its active share link.
/// </summary>
public class JoinSharedWishlistCommand : IRequest<WishlistParticipantJoinResult>, IGenericValidationFailure
{
    /// <summary>Initializes a shared-wishlist join command.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented share-link secret.</param>
    /// <param name="memberId">The optional authenticated member identifier.</param>
    /// <param name="guestToken">The optional browser guest token.</param>
    /// <param name="displayName">The optional anonymous display name.</param>
    public JoinSharedWishlistCommand(
        Guid shareLinkId,
        string? secret,
        Guid? memberId,
        string? guestToken,
        string? displayName)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
        MemberId = memberId;
        GuestToken = guestToken;
        DisplayName = displayName;
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

    /// <summary>Gets the optional anonymous display name.</summary>
    public string? DisplayName
    {
        get;
    }

    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {

        if (ShareLinkId == Guid.Empty || string.IsNullOrWhiteSpace(Secret))
            return new SharedWishlistNotFoundException();

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles joining a wishlist through its active share link.
/// </summary>
/// <param name="shareService">The wishlist share service.</param>
/// <param name="participantService">The wishlist participant service.</param>
/// <param name="logger">The logger.</param>
public class JoinSharedWishlistCommandHandler(
    IWishlistShareService shareService,
    IWishlistParticipantService participantService,
    ILogger<JoinSharedWishlistCommandHandler> logger)
    : IRequestHandler<JoinSharedWishlistCommand, WishlistParticipantJoinResult>
{
    /// <summary>Creates or resolves the current wishlist participant.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current participant and optional new guest token.</returns>
    public async Task<WishlistParticipantJoinResult> Handle(
        JoinSharedWishlistCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.WishlistParticipantJoinStarted(
            logger,
            request.ShareLinkId);
        var wishlist = await shareService.GetSharedAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        var result = await participantService.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlist.Id,
                ShareLinkId = request.ShareLinkId,
                ShareSecret = request.Secret ?? string.Empty,
                MemberId = request.MemberId,
                GuestToken = request.GuestToken,
                DisplayName = request.DisplayName
            },
            cancellationToken);
        ApplicationLogMessages.WishlistParticipantJoined(
            logger,
            wishlist.Id,
            result.Participant.Id);

        return result;
    }
}
