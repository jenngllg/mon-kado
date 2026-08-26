using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages the active share link of an owned wishlist.
/// </summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="authorizationService">The authorization service.</param>
/// <param name="entityTagService">The entity-tag service.</param>
/// <param name="urlService">The frontend share-link URL service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/wishlists/{wishlistId:guid}/share-link")]
public class WishlistShareLinksController(
    ISender sender,
    IAuthorizationService authorizationService,
    IEntityTagService entityTagService,
    IWishlistShareLinkUrlService urlService) : ControllerBase
{
    private const string GetShareLinkRouteName = "GetWishlistShareLink";

    /// <summary>Creates the active share link of an owned wishlist.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created share link.</returns>
    [HttpPost]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(WishlistShareLinkResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistShareLinkResponse>> CreateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var shareLink = await sender.Send(
            new CreateWishlistShareLinkCommand(
                GetMemberId(),
                wishlistId),
            cancellationToken);
        var response = CreateResponse(shareLink);
        Response.Headers.ETag = entityTagService.Format(shareLink.Version);
        Response.Headers.CacheControl = "no-store";

        return CreatedAtRoute(
            GetShareLinkRouteName,
            new
            {
                wishlistId
            },
            response);
    }

    /// <summary>Gets the active share link of an owned wishlist.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active share link.</returns>
    [HttpGet(Name = GetShareLinkRouteName)]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistShareLinkResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistShareLinkResponse>> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var shareLink = await sender.Send(
            new GetWishlistShareLinkQuery(
                GetMemberId(),
                wishlistId),
            cancellationToken);
        Response.Headers.ETag = entityTagService.Format(shareLink.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(CreateResponse(shareLink));
    }

    /// <summary>Regenerates the active share-link secret.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The regenerated share link.</returns>
    [HttpPut]
    [EntityTag(isRequired: true)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistShareLinkResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistShareLinkResponse>> RotateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var shareLink = await sender.Send(
            new RotateWishlistShareLinkCommand(
                GetMemberId(),
                wishlistId,
                entityTagService.Parse(Request.Headers.IfMatch)),
            cancellationToken);
        Response.Headers.ETag = entityTagService.Format(shareLink.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(CreateResponse(shareLink));
    }

    /// <summary>Revokes the active share link.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after revocation.</returns>
    [HttpDelete]
    [EntityTag(isRequired: true, returnsEntityTag: false)]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> DeleteAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        await sender.Send(
            new DeleteWishlistShareLinkCommand(
                GetMemberId(),
                wishlistId,
                entityTagService.Parse(Request.Headers.IfMatch)),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }

    private async Task AuthorizeWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizationService.AuthorizeAsync(
            User,
            wishlistId,
            AuthorizationPolicies.ManageWishlist);
        cancellationToken.ThrowIfCancellationRequested();

        if (!authorization.Succeeded)
            throw new WishlistNotFoundException();
    }

    private Guid GetMemberId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);

        return memberId;
    }

    private WishlistShareLinkResponse CreateResponse(WishlistShareLinkDetails shareLink)
    {
        return new WishlistShareLinkResponse(
            shareLink.Id,
            urlService.Build(
                shareLink.Id,
                shareLink.Secret),
            shareLink.CreatedAt,
            shareLink.UpdatedAt);
    }
}
