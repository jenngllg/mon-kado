using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages private wishlists owned by the current member.
/// </summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="authorizationService">The resource authorization service.</param>
/// <param name="entityTagService">The entity tag service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/wishlists")]
public class WishlistsController(
    ISender sender,
    IAuthorizationService authorizationService,
    IEntityTagService entityTagService) : ControllerBase
{
    private const string GetWishlistRouteName = "GetWishlist";
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Creates a private wishlist for the current member.
    /// </summary>
    /// <param name="request">The wishlist creation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wishlist.</returns>
    [HttpPost]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status201Created)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WishlistResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistResponse>> CreateAsync(
        CreateWishlistRequest request,
        CancellationToken cancellationToken)
    {
        var memberId = GetMemberId();
        var wishlist = await sender.Send(
            new CreateWishlistCommand(
                memberId,
                request.Name,
                request.Occasion,
                request.EventDate,
                request.Message),
            cancellationToken);
        var response = CreateResponse(wishlist);
        Response.Headers.ETag = entityTagService.Format(wishlist.Version);
        Response.Headers.CacheControl = "no-store";

        return CreatedAtRoute(
            GetWishlistRouteName,
            new
            {
                wishlistId = wishlist.Id
            },
            response);
    }

    /// <summary>
    /// Gets a private wishlist owned by the current member.
    /// </summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The private wishlist.</returns>
    [HttpGet(
        "{wishlistId:guid}",
        Name = GetWishlistRouteName)]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistResponse>> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizationService.AuthorizeAsync(
            User,
            wishlistId,
            AuthorizationPolicies.ManageWishlist);

        if (!authorization.Succeeded)
            throw new WishlistNotFoundException();

        var memberId = GetMemberId();
        var wishlist = await sender.Send(
            new GetWishlistQuery(
                memberId,
                wishlistId),
            cancellationToken);
        var response = CreateResponse(wishlist);
        Response.Headers.ETag = entityTagService.Format(wishlist.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    private Guid GetMemberId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);

        return memberId;
    }

    private static WishlistResponse CreateResponse(WishlistDetails wishlist)
    {
        return new WishlistResponse(
            wishlist.Id,
            wishlist.Name,
            wishlist.Occasion,
            wishlist.EventDate,
            wishlist.Message,
            wishlist.CreatedAt,
            wishlist.UpdatedAt);
    }
}
