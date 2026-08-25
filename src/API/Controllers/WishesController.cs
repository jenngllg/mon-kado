using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages gift wishes inside private wishlists owned by the current member.
/// </summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="authorizationService">The resource authorization service.</param>
/// <param name="entityTagService">The entity tag service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/wishlists/{wishlistId:guid}/wishes")]
public class WishesController(
    ISender sender,
    IAuthorizationService authorizationService,
    IEntityTagService entityTagService) : ControllerBase
{
    private const string GetWishRouteName = "GetWish";
    private const int MaximumRequestBodySize = 4 * 1024;
    private const int MaximumReorderRequestBodySize = 64 * 1024;

    /// <summary>
    /// Gets all gift wishes from an owned private wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete ordered gift wish collection.</returns>
    [HttpGet]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishCollectionResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishCollectionResponse>> GetCollectionAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var memberId = GetMemberId();
        var collection = await sender.Send(
            new GetWishesQuery(
                memberId,
                wishlistId),
            cancellationToken);
        var response = new WishCollectionResponse(collection.Wishes
            .Select(wish => new WishCollectionItemResponse(
                CreateResponse(wish),
                entityTagService.Format(wish.Version)))
            .ToArray());
        Response.Headers.ETag = entityTagService.Format(collection.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Replaces the complete order of gift wishes in an owned private wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="request">The complete requested order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated lightweight order.</returns>
    [HttpPatch]
    [EntityTag(isRequired: true)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [RequestSizeLimit(MaximumReorderRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WishOrderResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishOrderResponse>> ReorderAsync(
        Guid wishlistId,
        ReorderWishesRequest request,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var memberId = GetMemberId();
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        var order = await sender.Send(
            new ReorderWishesCommand(
                memberId,
                wishlistId,
                request.WishIds,
                expectedVersion),
            cancellationToken);
        var response = new WishOrderResponse(order.Wishes
            .Select(wish => new WishOrderItemResponse(
                wish.Id,
                wish.Position,
                entityTagService.Format(wish.Version)))
            .ToArray());
        Response.Headers.ETag = entityTagService.Format(order.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Adds a gift wish manually to an owned private wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="request">The wish creation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created gift wish.</returns>
    [HttpPost]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status201Created)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WishResponse), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishResponse>> CreateAsync(
        Guid wishlistId,
        CreateWishRequest request,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var memberId = GetMemberId();
        var wish = await sender.Send(
            new CreateWishCommand(
                memberId,
                wishlistId,
                request.Name,
                request.Note,
                request.Url,
                request.Price),
            cancellationToken);
        var response = CreateResponse(wish);
        Response.Headers.ETag = entityTagService.Format(wish.Version);
        Response.Headers.CacheControl = "no-store";

        return CreatedAtRoute(
            GetWishRouteName,
            new
            {
                wishlistId,
                wishId = wish.Id
            },
            response);
    }

    /// <summary>
    /// Gets one gift wish from an owned private wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested gift wish.</returns>
    [HttpGet(
        "{wishId:guid}",
        Name = GetWishRouteName)]
    [EntityTag]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WishResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishResponse>> GetAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var memberId = GetMemberId();
        var wish = await sender.Send(
            new GetWishQuery(
                memberId,
                wishlistId,
                wishId),
            cancellationToken);
        var response = CreateResponse(wish);
        Response.Headers.ETag = entityTagService.Format(wish.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Updates a gift wish in an owned private wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="request">The wish update request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated gift wish.</returns>
    [HttpPut("{wishId:guid}")]
    [EntityTag(isRequired: true)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WishResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<WishResponse>> UpdateAsync(
        Guid wishlistId,
        Guid wishId,
        UpdateWishRequest request,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var memberId = GetMemberId();
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        var wish = await sender.Send(
            new UpdateWishCommand(
                memberId,
                wishlistId,
                wishId,
                request.Name,
                request.Note,
                request.Url,
                request.Price,
                expectedVersion),
            cancellationToken);
        var response = CreateResponse(wish);
        Response.Headers.ETag = entityTagService.Format(wish.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Deletes a gift wish from an owned private wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after the gift wish is deleted.</returns>
    [HttpDelete("{wishId:guid}")]
    [EntityTag(isRequired: true, returnsEntityTag: false)]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> DeleteAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        await AuthorizeWishlistAsync(
            wishlistId,
            cancellationToken);
        var memberId = GetMemberId();
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        await sender.Send(
            new DeleteWishCommand(
                memberId,
                wishlistId,
                wishId,
                expectedVersion),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }

    /// <summary>
    /// Authorizes owner access to a private parent wishlist.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous authorization operation.</returns>
    /// <exception cref="WishlistNotFoundException">The private wishlist is unavailable to the current member.</exception>
    private async Task AuthorizeWishlistAsync(
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

    /// <summary>
    /// Gets the authenticated member identifier from the validated JWT.
    /// </summary>
    /// <returns>The authenticated member identifier.</returns>
    private Guid GetMemberId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);

        return memberId;
    }

    /// <summary>
    /// Maps application wish details to the API response.
    /// </summary>
    /// <param name="wish">The application wish details.</param>
    /// <returns>The API wish response.</returns>
    private static WishResponse CreateResponse(WishDetails wish)
    {
        return new WishResponse(
            wish.Id,
            wish.WishlistId,
            wish.Name,
            wish.Note,
            wish.Url,
            wish.Price,
            wish.Position,
            wish.CreatedAt,
            wish.UpdatedAt);
    }
}
