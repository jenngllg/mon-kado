using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Errors;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Delivers normalized gift images through short-lived signed URLs.
/// </summary>
/// <param name="urlService">The signed URL service.</param>
/// <param name="deliveryService">The current-state image delivery service.</param>
[ApiController]
[AllowAnonymous]
[Route("api/v1")]
public class WishImagesController(
    IWishImageUrlService urlService,
    IWishImageDeliveryService deliveryService) : ControllerBase
{
    private const string WebpContentType = "image/webp";

    /// <summary>
    /// Gets a current private gift image through a short-lived owner grant.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="token">The short-lived signed bearer grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized WebP image.</returns>
    [HttpGet("wishlists/{wishlistId:guid}/wishes/{wishId:guid}/image")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, WebpContentType)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> GetOwnedAsync(
        Guid wishlistId,
        Guid wishId,
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var grant = urlService.ValidateOwned(
            token,
            wishlistId,
            wishId);
        var stream = await deliveryService.OpenOwnedAsync(
            grant,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(
            stream,
            WebpContentType,
            enableRangeProcessing: false);
    }

    /// <summary>
    /// Gets a current gift image through a short-lived active share-link grant.
    /// </summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="token">The short-lived signed bearer grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized WebP image.</returns>
    [HttpGet("shared-wishlists/{shareLinkId:guid}/wishes/{wishId:guid}/image")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, WebpContentType)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> GetSharedAsync(
        Guid shareLinkId,
        Guid wishId,
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var grant = urlService.ValidateShared(
            token,
            shareLinkId,
            wishId);
        var stream = await deliveryService.OpenSharedAsync(
            grant,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(
            stream,
            WebpContentType,
            enableRangeProcessing: false);
    }
}
