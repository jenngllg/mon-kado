using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to update a gift wish.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="wishId">The wish identifier.</param>
/// <param name="name">The requested name.</param>
/// <param name="note">The optional owner note.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
/// <param name="expectedVersion">The version supplied by the client.</param>
/// <param name="quantity">The required total desired quantity.</param>
public class UpdateWishCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId,
    string? name,
    string? note,
    string? url,
    decimal? price,
    uint expectedVersion,
    int? quantity = null) : IRequest<WishDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated owner identifier.
    /// </summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid WishId { get; } = wishId;

    /// <summary>
    /// Gets the requested name.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Gets the optional owner note.
    /// </summary>
    public string? Note { get; } = note;

    /// <summary>
    /// Gets the optional product URL.
    /// </summary>
    public string? Url { get; } = url;

    /// <summary>
    /// Gets the optional price in euros.
    /// </summary>
    public decimal? Price { get; } = price;

    /// <summary>
    /// Gets the required total desired quantity.
    /// </summary>
    public int? Quantity { get; } = quantity;

    /// <summary>
    /// Gets the version supplied by the client.
    /// </summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        if (WishlistId == Guid.Empty)
            return new WishlistNotFoundException();

        if (WishId == Guid.Empty)
            return new WishNotFoundException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles gift wish update requests.
/// </summary>
/// <param name="wishService">The wish service.</param>
/// <param name="logger">The logger.</param>
public class UpdateWishCommandHandler(
    IWishService wishService,
    ILogger<UpdateWishCommandHandler> logger)
    : IRequestHandler<UpdateWishCommand, WishDetails>
{
    /// <summary>
    /// Updates a gift wish in an owned private wishlist.
    /// </summary>
    /// <param name="request">The wish update command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated wish.</returns>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the member.</exception>
    /// <exception cref="WishNotFoundException">The wish is unavailable under the parent wishlist.</exception>
    /// <exception cref="WishVersionConflictException">The wish version is stale.</exception>
    /// <exception cref="InvalidAuthenticationSessionException">The member was deleted during the update.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<WishDetails> Handle(
        UpdateWishCommand request,
        CancellationToken cancellationToken)
    {
        var name = WishTextNormalizer.NormalizeName(request.Name ?? string.Empty);
        var note = WishTextNormalizer.NormalizeNote(request.Note);
        var url = WishTextNormalizer.NormalizeUrl(request.Url);
        ApplicationLogMessages.WishUpdateStarted(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId);
        var wish = await wishService.UpdateAsync(
            request.OwnerId,
            request.WishlistId,
            request.WishId,
            name,
            note,
            url,
            request.Price,
            request.Quantity ?? WishTextValidation.MinimumQuantity,
            request.ExpectedVersion,
            cancellationToken);

        if (wish is null)
            throw new WishNotFoundException();

        ApplicationLogMessages.WishUpdated(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId);

        return wish;
    }
}
