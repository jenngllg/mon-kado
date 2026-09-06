using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Prepares editable merchant suggestions without creating a gift.</summary>
/// <param name="sender">The application mediator.</param>
/// <param name="authorizationService">The centralized resource authorization service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/wishlists/{wishlistId:guid}/wish-import-previews")]
public class WishImportPreviewsController(
    ISender sender,
    IAuthorizationService authorizationService) : ControllerBase
{
    /// <summary>Analyzes a public URL before the owner confirms gift creation.</summary>
    /// <remarks>
    /// Partial extraction returns warnings and editable suggestions. Keep this response only in memory.
    /// Confirm with POST wishes, then optionally PUT the decoded image using the returned ETag.
    /// If the image upload fails, retain the created wish and retry only the upload.
    /// No resource is persisted by this operation. Bearer authentication does not require antiforgery.
    /// </remarks>
    /// <param name="wishlistId">The owned parent wishlist.</param>
    /// <param name="request">The merchant URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Editable suggestions, an optional Base64 WebP image, and stable warning codes.</returns>
    [HttpPost]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.WishImportPreviewPolicy)]
    [RequestSizeLimit(16 * 1024)]
    [Consumes("application/json")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishImportPreview), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    // CurrentSession explicitly accepts JWT Bearer only, never ambient cookies; the request must be JSON.
    // codeql[cs/web/missing-token-validation]
    public async Task<ActionResult<WishImportPreview>> CreateAsync(
        Guid wishlistId,
        CreateWishImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var access = await authorizationService.AuthorizeAsync(
            User,
            wishlistId,
            AuthorizationPolicies.ModifyWishlist);
        cancellationToken.ThrowIfCancellationRequested();

        if (!access.Succeeded)
            throw new WishlistNotFoundException();
        var ownerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var preview = await sender.Send(
            new CreateWishImportPreviewCommand(
                ownerId,
                wishlistId,
                request.Url),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(preview);
    }
}
