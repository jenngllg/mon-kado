using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Exposes existing administrative events without extending their retention or recording new actions.</summary>
/// <param name="sender">The shared validation and query pipeline.</param>
[ApiController]
[PreserveRefreshCookie]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Authorize(Policy = AuthorizationPolicies.ViewAdministrativeAudit)]
[Route("api/v1/admin/audit-events")]
public class AdministrativeAuditController(ISender sender) : ControllerBase
{
    /// <summary>Retrieves a globally ordered page of retained administrative actions.</summary>
    /// <remarks>
    /// All filters are combined with AND. From is inclusive and to exclusive; both require ISO 8601 timestamps with an explicit timezone.
    /// RequestReference matches exactly, case-sensitively, after trimming. MemberId targets GDPR events, not moderated list owners.
    /// Defaults: page=1, pageSize=20, maximum=100. Missing targets and out-of-range pages return empty collections.
    /// Defaults apply only to absent parameters; supplied blank scalar filters return 400.
    /// Administrator display names are current, not historical. Deleted actors remain null and do not hide events.
    /// DownloadStarted records stream release, not successful archive receipt. Existing deletion and retention rules remain unchanged.
    /// No email, archive content, reservation data, or secrets are returned. No If-Match or antiforgery token is required.
    /// </remarks>
    /// <param name="request">The optional query filters and pagination.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested uncached audit page.</returns>
    [HttpGet]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaginatedResponse<AdministrativeAuditEventResponse>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PaginatedResponse<AdministrativeAuditEventResponse>>> GetPageAsync(
        [FromQuery] AdministrativeAuditRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetAdministrativeAuditEventsQuery
            {
                CallerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
                Action = request.Action,
                AdministratorId = request.AdministratorId,
                MemberId = request.MemberId,
                WishlistId = request.WishlistId,
                ExportId = request.ExportId,
                RequestReference = request.RequestReference,
                From = request.From,
                To = request.To,
                Page = request.Page,
                PageSize = request.PageSize
            },
            cancellationToken);
        var items = result.Items.Select(item => new AdministrativeAuditEventResponse
        {
            Id = item.Id,
            CreatedAt = item.CreatedAt,
            Action = item.Action,
            AdministratorId = item.AdministratorId,
            AdministratorDisplayName = item.AdministratorDisplayName,
            WishlistId = item.WishlistId,
            MemberId = item.MemberId,
            ExportId = item.ExportId,
            Reason = item.Reason,
            RequestReference = item.RequestReference
        });
        Response.Headers.CacheControl = "no-store";

        return Ok(new PaginatedResponse<AdministrativeAuditEventResponse>(
            items,
            result.CurrentPage,
            result.PageSize,
            result.TotalCount));
    }
}
