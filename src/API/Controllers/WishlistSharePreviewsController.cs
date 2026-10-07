using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using System.Text.Encodings.Web;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Exposes explicitly public title and image metadata for social previews.</summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="delivery">The current-state image delivery service.</param>
/// <param name="processor">The bounded social-image compositor.</param>
/// <param name="options">The trusted frontend-origin options.</param>
[ApiController]
[AllowAnonymous]
[Route("api/v1/shared-wishlists/{shareLinkId:guid}/preview")]
[EnableRateLimiting(AuthenticationRateLimitingExtensions.SharedWishlistPolicy)]
public class WishlistSharePreviewsController(
    ISender sender,
    IWishImageDeliveryService delivery,
    IWishlistSharePreviewImageProcessor processor,
    IOptions<WishlistSharingOptions> options) : ControllerBase
{
    private const string HtmlContentType = "text/html; charset=utf-8";
    private const string ImageContentType = "image/jpeg";

    /// <summary>Returns crawler-readable HTML containing only public preview metadata.</summary>
    /// <param name="shareLinkId">The active share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTML preview.</returns>
    [HttpGet]
    [HttpHead]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> GetAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        var preview = await sender.Send(
            new GetWishlistSharePreviewQuery(shareLinkId),
            cancellationToken);
        var title = HtmlEncoder.Default.Encode($"{preview.Name} | Ma liste sur MonKado");
        var images = string.Empty;

        if (preview.Images.Count > 0)
            images = $"""
                <meta property="og:image" content="{HtmlEncoder.Default.Encode(options.Value.FrontendOrigin + $"/share-previews/{shareLinkId:D}/image")}">
                <meta property="og:image:type" content="image/jpeg">
                <meta property="og:image:width" content="1200">
                <meta property="og:image:height" content="630">
                <meta property="og:image:alt" content="Aperçu des souhaits de cette liste">
                """;
        // Do not canonicalize the shared URL: its fragment must remain on the link shared by the user.
        var html = $"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8">
            <title>{title}</title>
            <meta property="og:type" content="website">
            <meta property="og:site_name" content="MonKado">
            <meta property="og:title" content="{title}">
            <meta property="og:description" content="Découvre cette liste de souhaits sur MonKado.">
            {images}
            </head><body><h1>{title}</h1></body></html>
            """;
        Response.Headers.CacheControl = "no-store";

        return Content(
            html,
            HtmlContentType);
    }

    /// <summary>Returns a bounded collage of current public-preview images.</summary>
    /// <param name="shareLinkId">The active share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized public-preview image.</returns>
    /// <exception cref="GiftImageNotFoundException">The image is not part of the current preview.</exception>
    [HttpGet("image")]
    [HttpHead("image")]
    [PublicImageProcessing]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, ImageContentType)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> GetImageAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        var preview = await sender.Send(
            new GetWishlistSharePreviewQuery(shareLinkId),
            cancellationToken);

        if (preview.Images.Count == 0)
            throw new GiftImageNotFoundException();
        var contents = new List<ReadOnlyMemory<byte>>();

        foreach (var image in preview.Images)
        {
            await using var stream = await delivery.OpenSharedAsync(
                new WishImageGrant
                {
                    ShareLinkId = shareLinkId,
                    WishlistId = preview.WishlistId,
                    WishId = image.WishId,
                    ImageId = image.ImageId
                },
                cancellationToken);
            using var content = new MemoryStream();
            await stream.CopyToAsync(
                content,
                cancellationToken);
            contents.Add(content.ToArray());
        }
        var composed = processor.Compose(
            contents,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return File(
            composed,
            ImageContentType,
            enableRangeProcessing: false);
    }
}
