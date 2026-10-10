using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Exposes member-owned subscriptions to revocable shared lists.</summary>
/// <param name="sender">The validated application pipeline.</param>
/// <param name="authorization">The resource authorization service.</param>
/// <param name="urls">The trusted share URL builder.</param>
/// <param name="tokens">The protected share-token service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/wishlist-subscriptions")]
public class WishlistSubscriptionsController(
    ISender sender,
    IAuthorizationService authorization,
    IWishlistShareLinkUrlService urls,
    IWishlistShareTokenService tokens) : ControllerBase
{
    private const string GetRouteName = "GetWishlistSubscription";
    private const string ShareTokenHeaderName = "X-MonKado-Share-Token";

    /// <summary>Subscribes the current member to a verified shared list.</summary>
    /// <param name="shareLinkId">The active share-link identifier.</param>
    /// <param name="shareToken">The secret received from the URL fragment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created subscription and its retrieval URL.</returns>
    [HttpPost("/api/v1/shared-wishlists/{shareLinkId:guid}/subscriptions")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistJoinPolicy)]
    [NoStoreResponse(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(WishlistSubscriptionResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistSubscriptionResponse>> CreateAsync(
        Guid shareLinkId,
        [FromHeader(Name = ShareTokenHeaderName)] string? shareToken,
        CancellationToken cancellationToken)
    {
        var subscription = await sender.Send(
            new CreateWishlistSubscriptionCommand(
                GetMemberId(),
                shareLinkId,
                shareToken),
            cancellationToken);

        return CreatedAtRoute(
            GetRouteName,
            new
            {
                id = subscription.Id
            },
            CreateResponse(subscription));
    }

    /// <summary>Gets the current member's subscription to a verified share link.</summary>
    /// <param name="shareLinkId">The active share-link identifier.</param>
    /// <param name="shareToken">The secret received from the URL fragment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current subscription, or a not-found error.</returns>
    [HttpGet("/api/v1/shared-wishlists/{shareLinkId:guid}/subscriptions/current")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistPolicy)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistSubscriptionResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistSubscriptionResponse>> GetCurrentAsync(
        Guid shareLinkId,
        [FromHeader(Name = ShareTokenHeaderName)] string? shareToken,
        CancellationToken cancellationToken)
    {
        var subscription = await sender.Send(
            new GetCurrentWishlistSubscriptionQuery(
                GetMemberId(),
                shareLinkId,
                shareToken),
            cancellationToken);

        return Ok(CreateResponse(subscription));
    }

    /// <summary>Gets a page of currently accessible followed lists.</summary>
    /// <param name="page">The optional one-based page.</param>
    /// <param name="pageSize">The optional page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page without wish or reservation content.</returns>
    [HttpGet]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaginatedResponse<WishlistSubscriptionResponse>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PaginatedResponse<WishlistSubscriptionResponse>>> GetPageAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetWishlistSubscriptionsQuery(
                GetMemberId(),
                page,
                pageSize),
            cancellationToken);

        return Ok(new PaginatedResponse<WishlistSubscriptionResponse>(
            result.Items.Select(CreateResponse),
            result.CurrentPage,
            result.PageSize,
            result.TotalCount));
    }

    /// <summary>Gets one current member-owned subscription.</summary>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The accessible subscription summary.</returns>
    [HttpGet("{id:guid}", Name = GetRouteName)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistSubscriptionResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistSubscriptionResponse>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(
            id,
            cancellationToken);
        var subscription = await sender.Send(
            new GetWishlistSubscriptionQuery(
                GetMemberId(),
                id),
            cancellationToken);

        return Ok(CreateResponse(subscription));
    }

    /// <summary>Removes a current member-owned subscription without affecting reservations.</summary>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after successful removal.</returns>
    [HttpDelete("{id:guid}")]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(
            id,
            cancellationToken);
        await sender.Send(
            new DeleteWishlistSubscriptionCommand(
                GetMemberId(),
                id),
            cancellationToken);

        return NoContent();
    }

    /// <summary>Authorizes access without revealing another member's subscription.</summary>
    /// <param name="id">The target subscription.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The authorization task.</returns>
    /// <exception cref="WishlistSubscriptionNotFoundException">The subscription is not accessible.</exception>
    private async Task AuthorizeAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await authorization.AuthorizeAsync(
            User,
            id,
            AuthorizationPolicies.ManageWishlistSubscription);

        if (!result.Succeeded)
            throw new WishlistSubscriptionNotFoundException();
    }

    /// <summary>Gets the subject of the authenticated current session.</summary>
    /// <returns>The authenticated member identifier.</returns>
    private Guid GetMemberId()
    {

        if (!Guid.TryParse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var memberId) || memberId == Guid.Empty)
            throw new InvalidAuthenticationSessionException();

        return memberId;
    }

    /// <summary>Constructs a response without exposing protected secrets or fingerprints.</summary>
    /// <param name="subscription">The revalidated subscription summary.</param>
    /// <returns>The public response.</returns>
    private WishlistSubscriptionResponse CreateResponse(WishlistSubscriptionDetails subscription)
    {

        return new WishlistSubscriptionResponse
        {
            Id = subscription.Id,
            WishlistId = subscription.WishlistId,
            Name = subscription.Name,
            OwnerDisplayName = subscription.OwnerDisplayName,
            Occasion = subscription.Occasion,
            EventDate = subscription.EventDate,
            CreatedAt = subscription.CreatedAt,
            ShareUrl = urls.Build(
                subscription.ShareLinkId,
                tokens.Unprotect(subscription.ProtectedSecret))
        };
    }
}
