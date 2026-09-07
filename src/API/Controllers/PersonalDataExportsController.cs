using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Results;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Requests and downloads private exports belonging exclusively to the authenticated member.</summary>
/// <param name="sender">The mediator sender.</param>
[ApiController]
[PreserveRefreshCookie]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/members/current/data-exports")]
public class PersonalDataExportsController(ISender sender) : ControllerBase
{
    /// <summary>Requests a ZIP containing retained personal data and current owned images.</summary>
    /// <remarks>
    /// No request body, extra password proof, antiforgery token or If-Match is required.
    /// Reuses a queued, processing or unexpired ready export without extending its lifetime.
    /// A new generation returns 202; reuse of a ready archive returns 200.
    /// Location identifies the owned status endpoint. At most three new requests are allowed per rolling 24 hours.
    /// An email notification is queued when the archive becomes ready; it remains available for 24 hours from publication.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created or reused export metadata.</returns>
    [HttpPost]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [NoStoreResponse(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PersonalDataExportResponse>> RequestAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RequestPersonalDataExportCommand(GetMemberId()),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Location = $"/api/v1/members/current/data-exports/{result.Id:D}";

        return StatusCode(
            result.Status is PersonalDataExportStatus.Ready ? StatusCodes.Status200OK : StatusCodes.Status202Accepted,
            MapResponse(result));
    }

    /// <summary>Gets the current member's latest retained export request.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest export metadata, or 404 when no request is retained.</returns>
    [HttpGet("latest")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PersonalDataExportResponse>> GetLatestAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetPersonalDataExportQuery(
                GetMemberId(),
                null),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(MapResponse(result));
    }

    /// <summary>Gets an owned export's lifecycle without exposing another member's requests.</summary>
    /// <remarks>A failed generation still returns 200 with status failed and its stable errorCode.</remarks>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owned request metadata.</returns>
    [HttpGet("{exportId:guid}")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PersonalDataExportResponse>> GetAsync(
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetPersonalDataExportQuery(
                GetMemberId(),
                exportId),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(MapResponse(result));
    }

    /// <summary>Downloads a complete private ZIP using Bearer authentication on every request.</summary>
    /// <remarks>
    /// Returns an attachment with Cache-Control: no-store and X-Content-Type-Options: nosniff.
    /// Unknown, foreign and expired archives return 404; unfinished or failed generations return 409.
    /// Storage failure returns 503 only before the response starts; a later read failure aborts the download.
    /// A download already started may finish after expiration. No signed or anonymous download URL is created.
    /// </remarks>
    /// <param name="exportId">The owned export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete archive as an attachment.</returns>
    [HttpGet("{exportId:guid}/archive")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/zip")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> DownloadAsync(
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DownloadPersonalDataExportQuery(
                GetMemberId(),
                exportId),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        return new PersonalDataExportFileResult(result);
    }

    /// <summary>Reads only the middleware-validated JWT subject.</summary>
    /// <returns>The authenticated account identifier.</returns>
    private Guid GetMemberId()
    {
        _ = Guid.TryParse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var memberId);

        return memberId;
    }

    /// <summary>Maps lifecycle metadata to the exact public contract.</summary>
    /// <param name="details">The owned lifecycle details.</param>
    /// <returns>The public response with a bounded terminal failure code.</returns>
    private static PersonalDataExportResponse MapResponse(PersonalDataExportDetails details)
    {

        return new PersonalDataExportResponse
        {
            Id = details.Id,
            Status = details.Status,
            CreatedAt = details.CreatedAt,
            SnapshotAt = details.SnapshotAt,
            ReadyAt = details.ReadyAt,
            ExpiresAt = details.ExpiresAt,
            SizeInBytes = details.SizeInBytes,
            ErrorCode = details.Failure switch
            {
                PersonalDataExportFailure.GenerationFailed => ErrorCodes.MemberDataExportGenerationFailed,
                PersonalDataExportFailure.TooLarge => ErrorCodes.MemberDataExportTooLarge,
                _ => null
            }
        };
    }
}
