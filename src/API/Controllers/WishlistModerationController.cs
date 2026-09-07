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

/// <summary>Provides administrator-only wishlist moderation and private decision history.</summary>
/// <param name="sender">The validated application request dispatcher.</param>
/// <param name="entityTagService">The optimistic concurrency header service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ModerateWishlist)]
[Route("api/v1/admin/wishlists/{wishlistId:guid}/moderation")]
public class WishlistModerationController(
    ISender sender,
    IEntityTagService entityTagService) : ControllerBase
{
    /// <summary>Retrieves the current state and private reason of a wishlist suspension.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current moderation state and wishlist ETag.</returns>
    [HttpGet]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistModerationResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistModerationResponse>> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetWishlistModerationQuery(wishlistId),
            cancellationToken);
        SetResponseHeaders(result.Version);

        return Ok(CreateResponse(result));
    }

    /// <summary>Suspends, amends a suspension reason, or reactivates a wishlist as an administrator.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="request">The requested state and private suspension reason.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated moderation state and wishlist ETag.</returns>
    [HttpPut]
    [Consumes("application/json")]
    [RequestSizeLimit(16 * 1024)]
    [EntityTag(isRequired: true)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishlistModerationResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishlistModerationResponse>> UpdateAsync(
        Guid wishlistId,
        UpdateWishlistModerationRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var administratorId);
        var version = entityTagService.Parse(Request.Headers.IfMatch);
        var result = await sender.Send(
            new UpdateWishlistModerationCommand(
                administratorId,
                wishlistId,
                request.IsSuspended,
                request.Reason,
                version),
            cancellationToken);
        SetResponseHeaders(result.Version);

        return Ok(CreateResponse(result));
    }

    /// <summary>Retrieves administrator-only decision history in reverse chronological order.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="page">The optional one-based page number, defaulting to one.</param>
    /// <param name="pageSize">The optional page size, defaulting to twenty and limited to one hundred.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of private decisions.</returns>
    [HttpGet("events")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaginatedResponse<WishlistModerationEventResponse>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<PaginatedResponse<WishlistModerationEventResponse>>> GetEventsAsync(
        Guid wishlistId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetWishlistModerationEventsQuery(
                wishlistId,
                page,
                pageSize),
            cancellationToken);
        var items = result.Items
            .Select(decision => new WishlistModerationEventResponse
            {
                Id = decision.Id,
                AdministratorId = decision.AdministratorId,
                Action = decision.Action,
                Reason = decision.Reason,
                OccurredAt = decision.OccurredAt
            })
            .ToArray();
        Response.Headers.CacheControl = "no-store";

        return Ok(new PaginatedResponse<WishlistModerationEventResponse>(
                items,
                result.CurrentPage,
                result.PageSize,
                result.TotalCount));
    }

    /// <summary>Writes uncached moderation response headers.</summary>
    /// <param name="version">The current wishlist version.</param>
    private void SetResponseHeaders(uint version)
    {
        Response.Headers.ETag = entityTagService.Format(version);
        Response.Headers.CacheControl = "no-store";
    }

    /// <summary>Maps private moderation state to its HTTP representation.</summary>
    /// <param name="details">The current application state.</param>
    /// <returns>The administrative response.</returns>
    private static WishlistModerationResponse CreateResponse(WishlistModerationDetails details)
    {

        return new WishlistModerationResponse
        {
            WishlistId = details.WishlistId,
            IsSuspended = details.IsSuspended,
            SuspensionReason = details.SuspensionReason,
            SuspendedAt = details.SuspendedAt
        };
    }
}
