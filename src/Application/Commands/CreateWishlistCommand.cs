using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to create a private wishlist.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="name">The requested name.</param>
/// <param name="occasion">The requested occasion.</param>
/// <param name="eventDate">The optional event date.</param>
/// <param name="message">The optional owner message.</param>
public class CreateWishlistCommand(
    Guid ownerId,
    string? name,
    WishlistOccasion? occasion,
    DateOnly? eventDate,
    string? message) : IRequest<WishlistDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated owner identifier.
    /// </summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>
    /// Gets the requested name.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Gets the requested occasion.
    /// </summary>
    public WishlistOccasion? Occasion { get; } = occasion;

    /// <summary>
    /// Gets the optional event date.
    /// </summary>
    public DateOnly? EventDate { get; } = eventDate;

    /// <summary>
    /// Gets the optional owner message.
    /// </summary>
    public string? Message { get; } = message;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        if (OwnerId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles private wishlist creation requests.
/// </summary>
/// <param name="wishlistService">The wishlist service.</param>
/// <param name="logger">The logger.</param>
public class CreateWishlistCommandHandler(
    IWishlistService wishlistService,
    ILogger<CreateWishlistCommandHandler> logger)
    : IRequestHandler<CreateWishlistCommand, WishlistDetails>
{
    /// <summary>
    /// Creates a private wishlist for the authenticated member.
    /// </summary>
    /// <param name="request">The wishlist creation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created wishlist.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    /// <exception cref="WishlistNameAlreadyExistsException">The member already owns the requested name.</exception>
    public async Task<WishlistDetails> Handle(
        CreateWishlistCommand request,
        CancellationToken cancellationToken)
    {
        var wishlistId = Guid.CreateVersion7();
        var name = WishlistTextNormalizer.NormalizeName(request.Name ?? string.Empty);
        var normalizedName = WishlistTextNormalizer.NormalizeNameForUniqueness(name);
        var message = WishlistTextNormalizer.NormalizeMessage(request.Message);
        ApplicationLogMessages.WishlistCreationStarted(
            logger,
            request.OwnerId,
            wishlistId);
        var wishlist = await wishlistService.CreateAsync(
            wishlistId,
            request.OwnerId,
            name,
            normalizedName,
            request.Occasion ?? default,
            request.EventDate,
            message,
            cancellationToken);

        if (wishlist is null)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.WishlistCreated(
            logger,
            request.OwnerId,
            wishlistId);

        return wishlist;
    }
}
