using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Provides private administrative report decisions and their immutable history.</summary>
/// <param name="sender">The validated application dispatcher.</param>
/// <param name="entityTagService">The report concurrency header service.</param>
[ApiController]
[Route("api/v1/admin/reported-wishlists/{wishlistId:guid}/reports/{reportId:guid}")]
public class WishlistReportReviewsController(
    ISender sender,
    IEntityTagService entityTagService) : ControllerBase
{
    private const string NoStoreCacheControl = "no-store";
    private const int MaximumRequestBodySize = 16 * 1024;

    /// <summary>Reads a report in any disposition and its own ETag, independently of the wishlist version.</summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current report and its ETag.</returns>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ViewWishlistReports)]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistReportDetails), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistReportDetails>> GetAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetWishlistReportQuery(
                wishlistId,
                reportId),
            cancellationToken);
        SetResponseHeaders(result.Version);

        return Ok(result.Report);
    }

    /// <summary>Reviews, corrects, or reopens one report without changing wishlist moderation or sending notifications.</summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="request">The required disposition and optional private note. An omitted, null, or blank note clears the current note.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated report and its ETag.</returns>
    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.ProcessWishlistReports)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [EntityTag(isRequired: true)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistReportDetails), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistReportDetails>> UpdateAsync(
        Guid wishlistId,
        Guid reportId,
        UpdateWishlistReportReviewRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var administratorId);
        var version = entityTagService.Parse(Request.Headers.IfMatch);
        var result = await sender.Send(
            new UpdateWishlistReportReviewCommand(
                administratorId,
                wishlistId,
                reportId,
                request.Status,
                request.ReviewNote,
                version),
            cancellationToken);
        SetResponseHeaders(result.Version);

        return Ok(result.Report);
    }

    /// <summary>Reads the private history of one report, including corrections and reopening.</summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="page">The optional one-based page, defaulting to one.</param>
    /// <param name="pageSize">The optional page size, defaulting to twenty and limited to one hundred.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The private review history in reverse sequence order.</returns>
    [HttpGet("events")]
    [Authorize(Policy = AuthorizationPolicies.ViewWishlistReports)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaginatedResponse<WishlistReportReviewEventDetails>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PaginatedResponse<WishlistReportReviewEventDetails>>> GetEventsAsync(
        Guid wishlistId,
        Guid reportId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetWishlistReportReviewEventsQuery(
                wishlistId,
                reportId,
                page,
                pageSize),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;

        return Ok(new PaginatedResponse<WishlistReportReviewEventDetails>(
                result.Items,
                result.CurrentPage,
                result.PageSize,
                result.TotalCount));
    }

    /// <summary>Writes uncached report-specific concurrency headers.</summary>
    /// <param name="version">The report version.</param>
    private void SetResponseHeaders(uint version)
    {
        Response.Headers.CacheControl = NoStoreCacheControl;
        Response.Headers.ETag = entityTagService.Format(version);
    }
}
