using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Manages one normalized public photo per member account.</summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="entityTagService">The account ETag service.</param>
/// <param name="profileImageUrlService">The public photo URL service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/members")]
public class ProfileImagesController(
    ISender sender,
    IEntityTagService entityTagService,
    IProfileImageUrlService profileImageUrlService) : ControllerBase
{
    private const int MaximumImageRequestBodySize = GiftImageConstraints.MaximumInputLength + 64 * 1024;
    /// <summary>Adds or replaces the current member's public profile photo.</summary>
    /// <param name="image">Exactly one JPEG, PNG or non-animated WebP file, at most 10 MiB.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete profile with its current photo URL and account ETag.</returns>
    [HttpPut("current/profile/image")]
    [ProfileImageUpload]
    [EntityTag(isRequired: true)]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.ProfileImageUploadPolicy)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5693:Request content length should be limited", Justification = "MK-876 permits 10 MiB images with bounded multipart overhead; authenticated uploads are rate limited and decoded dimensions are capped.")]
    [RequestSizeLimit(MaximumImageRequestBodySize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumImageRequestBodySize)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MemberProfileResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<MemberProfileResponse>> UpsertAsync(
        [FromForm(Name = "image")] IFormFile? image,
        CancellationToken cancellationToken)
    {
        var memberId = GetMemberId();
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        var content = await ReadImageAsync(
            image,
            cancellationToken);
        var form = await Request.ReadFormAsync(cancellationToken);
        var validShape = form.Files.Count == 1 && form.Count == 0 && string.Equals(
            image?.Name,
            "image",
            StringComparison.Ordinal);
        var profile = await sender.Send(
            new UpsertProfileImageCommand(
                memberId,
                content,
                expectedVersion,
                validShape),
            cancellationToken);
        var response = new MemberProfileResponse(profile.DisplayName)
        {
            ProfileImageUrl = profileImageUrlService.CreateUrl(
                memberId,
                profile.ProfileImageId)
        };
        Response.Headers.ETag = entityTagService.Format(profile.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>Removes the current member's photo and schedules durable file cleanup.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content and the new account ETag.</returns>
    [HttpDelete("current/profile/image")]
    [EntityTag(isRequired: true)]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> DeleteAsync(CancellationToken cancellationToken)
    {
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        var version = await sender.Send(
            new DeleteProfileImageCommand(
                GetMemberId(),
                expectedVersion),
            cancellationToken);
        Response.Headers.ETag = entityTagService.Format(version);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }

    /// <summary>Reads the current public photo of a confirmed member; old photo URLs return 404.</summary>
    /// <param name="memberId">The public member identifier.</param>
    /// <param name="imageId">The current photo identifier from its versioned URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized WebP image without browser caching.</returns>
    [HttpGet("{memberId:guid}/profile/image")]
    [AllowAnonymous]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/webp")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> GetAsync(
        Guid memberId,
        [FromQuery] Guid? imageId,
        CancellationToken cancellationToken)
    {
        var stream = await sender.Send(
            new GetProfileImageQuery(
                memberId,
                imageId),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(
            stream,
            "image/webp");
    }

    /// <summary>Reads the authenticated member identifier from the minimal JWT.</summary>
    /// <returns>The member identifier, validated by the application pipeline.</returns>
    private Guid GetMemberId()
    {
        _ = Guid.TryParse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var memberId);

        return memberId;
    }

    /// <summary>Reads a bounded upload without trusting its original filename or declared type.</summary>
    /// <param name="image">The supplied multipart file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The uploaded bytes, or null when the field is absent.</returns>
    /// <exception cref="BadHttpRequestException">The image exceeds the maximum input size.</exception>
    private static async Task<byte[]?> ReadImageAsync(
        IFormFile? image,
        CancellationToken cancellationToken)
    {

        if (image is null)
            return null;

        if (image.Length > GiftImageConstraints.MaximumInputLength)
        {

            throw new BadHttpRequestException(
                "The profile image is too large.",
                StatusCodes.Status413PayloadTooLarge);
        }

        await using var source = image.OpenReadStream();
        await using var target = new MemoryStream((int)image.Length);
        await source.CopyToAsync(
            target,
            cancellationToken);

        return target.ToArray();
    }
}
