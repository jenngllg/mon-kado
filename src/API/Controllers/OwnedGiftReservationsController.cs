using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Manages only the authenticated owner's personal wish reservation without shared access.</summary>
/// <param name="sender">The application pipeline.</param>
/// <param name="authorizationService">The named resource authorization service.</param>
/// <param name="entityTagService">The reservation version service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/wishlists/{wishlistId:guid}/wishes/{wishId:guid}/reservations/current")]
public class OwnedGiftReservationsController(
    ISender sender,
    IAuthorizationService authorizationService,
    IEntityTagService entityTagService) : ControllerBase
{
    private const string GetReservationRouteName = "GetOwnedGiftReservation";
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>Gets the owner's reservation for an owned wish.</summary>
    /// <param name="wishlistId">The owned parent.</param>
    /// <param name="wishId">The owned wish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The personal reservation, never other participants' reservations.</returns>
    [HttpGet(Name = GetReservationRouteName)]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GiftReservationResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<GiftReservationResponse>> GetAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        await AuthorizeParentAsync(
            wishlistId,
            cancellationToken);
        var reservation = await sender.Send(
            new GetOwnedGiftReservationQuery(
                GetOwnerId(),
                wishlistId,
                wishId),
            cancellationToken);
        Response.Headers.ETag = entityTagService.Format(reservation.Version);

        return Ok(CreateResponse(reservation));
    }

    /// <summary>Creates or replaces the owner's reservation, sharing capacity with all participants.</summary>
    /// <param name="wishlistId">The owned parent.</param>
    /// <param name="wishId">The owned wish.</param>
    /// <param name="request">The absolute quantity.</param>
    /// <param name="expectedEntityTag">The optional current reservation version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created or updated personal reservation.</returns>
    [HttpPut]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.OwnedGiftReservationPolicy)]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [NoStoreResponse(StatusCodes.Status201Created)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(GiftReservationResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(GiftReservationResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<GiftReservationResponse>> UpsertAsync(
        Guid wishlistId,
        Guid wishId,
        UpsertGiftReservationRequest request,
        [FromHeader(Name = "If-Match")] string? expectedEntityTag,
        CancellationToken cancellationToken)
    {
        await AuthorizeParentAsync(
            wishlistId,
            cancellationToken);
        var result = await sender.Send(
            new UpsertOwnedGiftReservationCommand(
                GetOwnerId(),
                wishlistId,
                wishId,
                request.Quantity,
                entityTagService.ParseOptional(expectedEntityTag)),
            cancellationToken);
        Response.Headers.ETag = entityTagService.Format(result.Reservation.Version);
        var response = CreateResponse(result.Reservation);

        if (!result.IsCreated)
            return Ok(response);

        return CreatedAtRoute(
            GetReservationRouteName,
            new
            {
                wishlistId,
                wishId
            },
            response);
    }

    /// <summary>Cancels only the owner's reservation with its current version.</summary>
    /// <param name="wishlistId">The owned parent.</param>
    /// <param name="wishId">The owned wish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after cancellation.</returns>
    [HttpDelete]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.OwnedGiftReservationPolicy)]
    [EntityTag(isRequired: true, returnsEntityTag: false)]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> CancelAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        await AuthorizeParentAsync(
            wishlistId,
            cancellationToken);
        await sender.Send(
            new CancelOwnedGiftReservationCommand(
                GetOwnerId(),
                wishlistId,
                wishId,
                entityTagService.Parse(Request.Headers.IfMatch)),
            cancellationToken);

        return NoContent();
    }

    /// <summary>Uses the existing private parent policy without disclosing foreign resources.</summary>
    /// <param name="wishlistId">The requested parent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the parent is authorized.</returns>
    private async Task AuthorizeParentAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizationService.AuthorizeAsync(
            User,
            wishlistId,
            AuthorizationPolicies.ManageWishlist);
        cancellationToken.ThrowIfCancellationRequested();

        if (!authorization.Succeeded)
            throw new WishlistNotFoundException();
    }

    /// <summary>Gets the member from the validated session.</summary>
    /// <returns>The authenticated owner identifier.</returns>
    private Guid GetOwnerId()
    {
        _ = Guid.TryParse(
            User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var ownerId);

        return ownerId;
    }

    /// <summary>Projects only the owner's personal reservation.</summary>
    /// <param name="reservation">The personal result.</param>
    /// <returns>The public reservation contract.</returns>
    private static GiftReservationResponse CreateResponse(GiftReservationDetails reservation)
    {

        return new GiftReservationResponse
        {
            Id = reservation.Id,
            WishId = reservation.WishId,
            Quantity = reservation.Quantity,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt
        };
    }
}
