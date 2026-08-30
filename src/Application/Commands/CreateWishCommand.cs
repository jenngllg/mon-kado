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
/// Represents a request to add a gift wish manually.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="name">The requested name.</param>
/// <param name="note">The optional owner note.</param>
/// <param name="url">The optional product URL.</param>
/// <param name="price">The optional price in euros.</param>
/// <param name="quantity">The optional total desired quantity.</param>
public class CreateWishCommand(
    Guid ownerId,
    Guid wishlistId,
    string? name,
    string? note,
    string? url,
    decimal? price,
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
    /// Gets the optional total desired quantity.
    /// </summary>
    public int? Quantity { get; } = quantity;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        if (WishlistId == Guid.Empty)
            return new WishlistNotFoundException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles manual gift wish creation requests.
/// </summary>
/// <param name="wishService">The wish service.</param>
/// <param name="logger">The logger.</param>
public class CreateWishCommandHandler(
    IWishService wishService,
    ILogger<CreateWishCommandHandler> logger)
    : IRequestHandler<CreateWishCommand, WishDetails>
{
    /// <summary>
    /// Creates a gift wish in an owned private wishlist.
    /// </summary>
    /// <param name="request">The wish creation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wish.</returns>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the member.</exception>
    public async Task<WishDetails> Handle(
        CreateWishCommand request,
        CancellationToken cancellationToken)
    {
        var wishId = Guid.CreateVersion7();
        var name = WishTextNormalizer.NormalizeName(request.Name ?? string.Empty);
        var note = WishTextNormalizer.NormalizeNote(request.Note);
        var url = WishTextNormalizer.NormalizeUrl(request.Url);
        ApplicationLogMessages.WishCreationStarted(
            logger,
            request.OwnerId,
            request.WishlistId,
            wishId);
        var wish = await wishService.CreateAsync(
            wishId,
            request.OwnerId,
            request.WishlistId,
            name,
            note,
            url,
            request.Price,
            request.Quantity ?? WishTextValidation.MinimumQuantity,
            cancellationToken);

        if (wish is null)
            throw new WishlistNotFoundException();

        ApplicationLogMessages.WishCreated(
            logger,
            request.OwnerId,
            request.WishlistId,
            wishId);

        return wish;
    }
}
