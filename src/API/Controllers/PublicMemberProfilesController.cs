using JennGllg.Fr.MonKado.Back.Api.Abstractions;
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

/// <summary>Exposes confirmed member profiles and actively shared lists to visitors.</summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="photoUrls">The trusted public photo URL service.</param>
/// <param name="shareUrls">The trusted frontend share URL service.</param>
[ApiController]
[AllowAnonymous]
[Route("api/v1/members")]
public class PublicMemberProfilesController(
    ISender sender,
    IProfileImageUrlService photoUrls,
    IWishlistShareLinkUrlService shareUrls) : ControllerBase
{
    /// <summary>Gets the public profile of a confirmed member and their active, non-suspended shared lists.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The minimal public profile and discoverable list summaries.</returns>
    [HttpGet("{memberId:guid}/profile")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.UserSearchPolicy)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PublicMemberProfileResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PublicMemberProfileResponse>> GetAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var profile = await sender.Send(
            new GetPublicMemberProfileQuery(memberId),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(new PublicMemberProfileResponse
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            ProfileImageUrl = photoUrls.CreateUrl(
                profile.Id,
                profile.ProfileImageId),
            Wishlists = profile.Wishlists
                .Select(wishlist => new PublicMemberWishlistResponse
                {
                    Id = wishlist.Id,
                    Name = wishlist.Name,
                    Occasion = wishlist.Occasion,
                    EventDate = wishlist.EventDate,
                    ShareUrl = shareUrls.Build(
                        wishlist.ShareLinkId,
                        wishlist.Secret)
                })
                .ToArray()
        });
    }
}
