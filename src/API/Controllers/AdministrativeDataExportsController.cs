using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Mappers;
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

/// <summary>Produces audited administrative exports without changing the member's private access policy.</summary>
/// <param name="sender">The mediator sender.</param>
[ApiController]
[PreserveRefreshCookie]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Authorize(Policy = AuthorizationPolicies.ExportMemberData)]
[Route("api/v1/admin/members/{memberId:guid}/data-exports")]
public class AdministrativeDataExportsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;
    private const string NoStoreCacheControl = "no-store";
    /// <summary>Requests or reuses a member's ZIP and durably records the administrative request.</summary>
    /// <remarks>
    /// Requires a technical requestReference of at most 128 characters without control characters or personal data.
    /// Every existing account is eligible, including unconfirmed accounts; only confirmed email addresses are notified.
    /// Reuses active exports and shares the member's three-generation rolling 24-hour quota.
    /// Archives expire 24 hours after publication. Audit events are retained for six calendar months.
    /// No additional password, antiforgery token or If-Match is required for these Bearer-only routes.
    /// </remarks>
    /// <param name="memberId">The target member identifier.</param>
    /// <param name="request">The external request reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created or reused export metadata and its administrative Location.</returns>
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [NoStoreResponse(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    // Both policies accept explicit JWT Bearer authentication, never ambient cookies.
    // codeql[cs/web/missing-token-validation]
    public async Task<ActionResult<PersonalDataExportResponse>> RequestAsync(
        Guid memberId,
        RequestAdministrativeDataExportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RequestAdministrativeDataExportCommand(
                GetAdministratorId(),
                memberId,
                request.RequestReference),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;
        Response.Headers.Location = $"/api/v1/admin/members/{memberId:D}/data-exports/{result.Id:D}";

        return StatusCode(
            result.Status is PersonalDataExportStatus.Ready ? StatusCodes.Status200OK : StatusCodes.Status202Accepted,
            PersonalDataExportResponseMapper.Map(result));
    }

    /// <summary>Gets the latest retained export requested administratively for the member.</summary>
    /// <param name="memberId">The target member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest administratively requested export metadata.</returns>
    [HttpGet("latest")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PersonalDataExportResponse>> GetLatestAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetAdministrativeDataExportQuery(
                GetAdministratorId(),
                memberId,
                null),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;

        return Ok(PersonalDataExportResponseMapper.Map(result));
    }

    /// <summary>Gets one retained export with an existing administrative request.</summary>
    /// <param name="memberId">The target member identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The administratively requested export metadata.</returns>
    [HttpGet("{exportId:guid}")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PersonalDataExportResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PersonalDataExportResponse>> GetAsync(
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetAdministrativeDataExportQuery(
                GetAdministratorId(),
                memberId,
                exportId),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;

        return Ok(PersonalDataExportResponseMapper.Map(result));
    }

    /// <summary>Downloads an administratively requested archive after durable release auditing.</summary>
    /// <remarks>
    /// Requires a live database-backed administrator role on every request. No public or signed download URL exists.
    /// Missing or expired archives return 404; unfinished or failed exports return 409.
    /// Audit or storage unavailability prevents release and returns 503 before headers; subsequent storage errors abort the stream.
    /// The audit records download initiation, not delivery to the member. Identity verification and secure handover remain separate.
    /// </remarks>
    /// <param name="memberId">The target member identifier.</param>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The private ZIP as an attachment.</returns>
    [HttpGet("{exportId:guid}/archive")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/zip")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> DownloadAsync(
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DownloadAdministrativeDataExportQuery(
                GetAdministratorId(),
                memberId,
                exportId),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;
        Response.Headers.XContentTypeOptions = "nosniff";

        return new PersonalDataExportFileResult(result);
    }

    /// <summary>Reads only the middleware-validated JWT subject.</summary>
    /// <returns>The authenticated administrator identifier.</returns>
    private Guid GetAdministratorId()
    {
        _ = Guid.TryParse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var administratorId);

        return administratorId;
    }
}
