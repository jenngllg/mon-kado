using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Provides read-only administrator access to anonymously reported wishlists.</summary>
/// <param name="sender">The validated request dispatcher.</param>
/// <param name="imageUrlService">The image route builder.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ViewWishlistReports)]
[Route("api/v1/admin/reported-wishlists")]
public class ReportedWishlistsController(
    ISender sender,
    IWishImageUrlService imageUrlService) : ControllerBase
{
    private const string NoStoreCacheControl = "no-store";

    /// <summary>Lists reported wishlists, counting and ordering only reports matching the optional reason filter.</summary>
    /// <param name="reason">The optional reason.</param>
    /// <param name="isSuspended">The optional isSuspended.</param>
    /// <param name="page">The optional page, defaulting to one.</param>
    /// <param name="pageSize">The optional pageSize, defaulting to twenty and limited to one hundred.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current read-only result.</returns>
    [HttpGet]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaginatedResponse<ReportedWishlistSummary>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    public async Task<ActionResult<PaginatedResponse<ReportedWishlistSummary>>> GetPageAsync(
        [FromQuery] WishlistReportReason? reason,
        [FromQuery] bool? isSuspended,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetReportedWishlistsQuery(
                reason,
                isSuspended,
                page,
                pageSize),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;

        return Ok(new PaginatedResponse<ReportedWishlistSummary>(
                result.Items,
                result.CurrentPage,
                result.PageSize,
                result.TotalCount));
    }

    /// <summary>Reads current reported content, including suspended wishlists and revoked shares, without reservations.</summary>
    /// <param name="wishlistId">The reported wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current read-only result.</returns>
    [HttpGet("{wishlistId:guid}")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReportedWishlistResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    public async Task<ActionResult<ReportedWishlistResponse>> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetReportedWishlistQuery(wishlistId),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;

        return Ok(new ReportedWishlistResponse
        {
            WishlistId = result.Wishlist.Id,
            Name = result.Wishlist.Name,
            OwnerId = result.OwnerId,
            OwnerDisplayName = result.OwnerDisplayName,
            Occasion = result.Wishlist.Occasion,
            EventDate = result.Wishlist.EventDate,
            Message = result.Wishlist.Message,
            CreatedAt = result.Wishlist.CreatedAt,
            UpdatedAt = result.Wishlist.UpdatedAt,
            IsSuspended = result.Wishlist.IsSuspended,
            SuspensionReason = result.Wishlist.SuspensionReason,
            SuspendedAt = result.Wishlist.SuspendedAt,
            Wishes = result.Wishes
                    .Select(wish => new ReportedWishResponse
                    {
                        Id = wish.Id,
                        Name = wish.Name,
                        Note = wish.Note,
                        Url = wish.Url,
                        Price = wish.Price,
                        Quantity = wish.Quantity,
                        Position = wish.Position,
                        CreatedAt = wish.CreatedAt,
                        UpdatedAt = wish.UpdatedAt,
                        ImageUrl = wish.ImageId.HasValue ? imageUrlService.CreateReportedUrl(
                            wishlistId,
                            wish.Id) : null
                    })
                    .ToArray()
        });
    }

    /// <summary>Lists anonymous reports in reverse chronological order.</summary>
    /// <param name="wishlistId">The reported wishlist identifier.</param>
    /// <param name="reason">The optional reason.</param>
    /// <param name="page">The optional page, defaulting to one.</param>
    /// <param name="pageSize">The optional pageSize, defaulting to twenty and limited to one hundred.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current read-only result.</returns>
    [HttpGet("{wishlistId:guid}/reports")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaginatedResponse<WishlistReportDetails>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    public async Task<ActionResult<PaginatedResponse<WishlistReportDetails>>> GetReportsAsync(
        Guid wishlistId,
        [FromQuery] WishlistReportReason? reason,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetWishlistReportsQuery(
                wishlistId,
                reason,
                page,
                pageSize),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;

        return Ok(new PaginatedResponse<WishlistReportDetails>(
                result.Items,
                result.CurrentPage,
                result.PageSize,
                result.TotalCount));
    }

    /// <summary>Reads the current WebP image. Administrator Bearer authentication is required on every request.</summary>
    /// <param name="wishlistId">The reported wishlist identifier.</param>
    /// <param name="wishId">The gift identifier within the reported wishlist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current read-only result.</returns>
    [HttpGet("{wishlistId:guid}/wishes/{wishId:guid}/image")]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, "image/webp")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    public async Task<IActionResult> GetImageAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetReportedWishImageQuery(
                wishlistId,
                wishId),
            cancellationToken);
        Response.Headers.CacheControl = NoStoreCacheControl;
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(
            result,
            "image/webp",
            enableRangeProcessing: false);
    }
}
