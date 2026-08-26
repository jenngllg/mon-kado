using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Exposes read-only wishlist content to holders of a valid share link.
/// </summary>
/// <param name="sender">The mediator sender.</param>
[ApiController]
[AllowAnonymous]
[Route("api/v1/shared-wishlists")]
public class SharedWishlistsController(ISender sender) : ControllerBase
{
    /// <summary>Identifies the bearer share-secret request header.</summary>
    public const string ShareTokenHeaderName = "X-MonKado-Share-Token";

    /// <summary>Gets a wishlist through its active share link.</summary>
    /// <param name="shareLinkId">The public share-link identifier.</param>
    /// <param name="shareToken">The bearer secret contained in the URL fragment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The publicly shared wishlist content.</returns>
    [HttpGet("{shareLinkId:guid}")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistPolicy)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SharedWishlistResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<SharedWishlistResponse>> GetAsync(
        Guid shareLinkId,
        [FromHeader(Name = ShareTokenHeaderName)] string? shareToken,
        CancellationToken cancellationToken)
    {
        var wishlist = await sender.Send(
            new GetSharedWishlistQuery(
                shareLinkId,
                shareToken),
            cancellationToken);
        var response = new SharedWishlistResponse(
            wishlist.Id,
            wishlist.OwnerDisplayName,
            wishlist.Name,
            wishlist.Occasion,
            wishlist.EventDate,
            wishlist.Message,
            wishlist.Wishes
                .Select(wish => new SharedWishResponse(
                    wish.Id,
                    wish.Name,
                    wish.Url,
                    wish.Price))
                .ToArray());
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }
}
