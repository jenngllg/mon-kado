using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Exposes shared wishlist content and participant operations to holders of a valid share link.
/// </summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="guestSessionCookieService">The guest-session cookie service.</param>
[ApiController]
[AllowAnonymous]
[Route("api/v1/shared-wishlists")]
public class SharedWishlistsController(
    ISender sender,
    IGuestSessionCookieService guestSessionCookieService) : ControllerBase
{
    private const string GetCurrentParticipantRouteName = "GetCurrentSharedWishlistParticipant";
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>Identifies the bearer share-secret request header.</summary>
    public const string ShareTokenHeaderName = "X-MonKado-Share-Token";

    /// <summary>Gets a wishlist through its active share link.</summary>
    /// <param name="shareLinkId">The public share-link identifier.</param>
    /// <param name="shareToken">The bearer secret contained in the URL fragment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The publicly shared wishlist content.</returns>
    [HttpGet("{shareLinkId:guid}")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistPolicy)]
    [OptionalBearer]
    [GuestSessionCookie(isRequired: false)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SharedWishlistResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<SharedWishlistResponse>> GetAsync(
        Guid shareLinkId,
        [FromHeader(Name = ShareTokenHeaderName)] string? shareToken,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetSharedWishlistQuery(
                shareLinkId,
                shareToken,
                GetOptionalMemberId(),
                guestSessionCookieService.GetValue(Request)),
            cancellationToken);
        var wishlist = result.Wishlist;
        var response = new SharedWishlistResponse
        {
            Id = wishlist.Id,
            OwnerDisplayName = wishlist.OwnerDisplayName,
            Name = wishlist.Name,
            Occasion = wishlist.Occasion,
            EventDate = wishlist.EventDate,
            Message = wishlist.Message,
            Wishes = wishlist.Wishes
                .Select(wish => new SharedWishResponse(
                    wish.Id,
                    wish.Name,
                    wish.Url,
                    wish.Price))
                .ToArray(),
            CurrentParticipant = result.CurrentParticipant is null
                ? null
                : CreateParticipantResponse(result.CurrentParticipant)
        };
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>Joins a wishlist through its active share link.</summary>
    /// <param name="shareLinkId">The public share-link identifier.</param>
    /// <param name="shareToken">The bearer secret contained in the URL fragment.</param>
    /// <param name="request">The optional anonymous participant details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant associated with the current caller.</returns>
    [HttpPost("{shareLinkId:guid}/participants")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistJoinPolicy)]
    [OptionalBearer]
    [GuestSessionCookie(isRequired: false, isReturned: true)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [NoStoreResponse(StatusCodes.Status201Created)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WishlistParticipantResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(WishlistParticipantResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistParticipantResponse>> JoinAsync(
        Guid shareLinkId,
        [FromHeader(Name = ShareTokenHeaderName)] string? shareToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JoinSharedWishlistRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new JoinSharedWishlistCommand(
                shareLinkId,
                shareToken,
                GetOptionalMemberId(),
                guestSessionCookieService.GetValue(Request),
                request?.DisplayName),
            cancellationToken);

        if (result.GuestToken is not null && result.GuestTokenExpiresAt is DateTime expiresAt)
        {
            guestSessionCookieService.Append(
                HttpContext,
                result.GuestToken,
                expiresAt);
        }

        var response = CreateParticipantResponse(result.Participant);
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store";

        if (!result.IsCreated)
            return Ok(response);

        return CreatedAtRoute(
            GetCurrentParticipantRouteName,
            new
            {
                shareLinkId
            },
            response);
    }

    /// <summary>Gets the participant associated with the current caller.</summary>
    /// <param name="shareLinkId">The public share-link identifier.</param>
    /// <param name="shareToken">The bearer secret contained in the URL fragment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant associated with the current caller.</returns>
    [HttpGet("{shareLinkId:guid}/participants/current", Name = GetCurrentParticipantRouteName)]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistPolicy)]
    [OptionalBearer]
    [GuestSessionCookie(isRequired: false)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistParticipantResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistParticipantResponse>> GetCurrentAsync(
        Guid shareLinkId,
        [FromHeader(Name = ShareTokenHeaderName)] string? shareToken,
        CancellationToken cancellationToken)
    {
        var participant = await sender.Send(
            new GetCurrentWishlistParticipantQuery(
                shareLinkId,
                shareToken,
                GetOptionalMemberId(),
                guestSessionCookieService.GetValue(Request)),
            cancellationToken);
        var response = CreateParticipantResponse(participant);
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    private Guid? GetOptionalMemberId()
    {
        if (User.Identity?.IsAuthenticated is not true)
            return null;

        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);

        return memberId;
    }

    private static WishlistParticipantResponse CreateParticipantResponse(
        WishlistParticipantDetails participant)
    {
        return new WishlistParticipantResponse(
            participant.Id,
            participant.DisplayName);
    }
}
