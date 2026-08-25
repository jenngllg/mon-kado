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
/// Represents a request to update a private wishlist.
/// </summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="name">The requested name.</param>
/// <param name="occasion">The requested occasion.</param>
/// <param name="eventDate">The optional event date.</param>
/// <param name="message">The optional owner message.</param>
/// <param name="expectedVersion">The version supplied by the client.</param>
public class UpdateWishlistCommand(
    Guid ownerId,
    Guid wishlistId,
    string? name,
    WishlistOccasion? occasion,
    DateOnly? eventDate,
    string? message,
    uint expectedVersion) : IRequest<WishlistDetails>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated owner identifier.
    /// </summary>
    public Guid OwnerId { get; } = ownerId;

    /// <summary>
    /// Gets the wishlist identifier.
    /// </summary>
    public Guid WishlistId { get; } = wishlistId;

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

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles private wishlist update requests.
/// </summary>
/// <param name="wishlistService">The wishlist service.</param>
/// <param name="logger">The logger.</param>
public class UpdateWishlistCommandHandler(
    IWishlistService wishlistService,
    ILogger<UpdateWishlistCommandHandler> logger)
    : IRequestHandler<UpdateWishlistCommand, WishlistDetails>
{
    /// <summary>
    /// Updates a private wishlist owned by the authenticated member.
    /// </summary>
    /// <param name="request">The wishlist update command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated wishlist.</returns>
    /// <exception cref="WishlistNotFoundException">The wishlist is unavailable to the member.</exception>
    /// <exception cref="WishlistNameAlreadyExistsException">The member already owns the requested name.</exception>
    /// <exception cref="WishlistVersionConflictException">The wishlist version is stale.</exception>
    /// <exception cref="InvalidAuthenticationSessionException">The member was deleted during the update.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<WishlistDetails> Handle(
        UpdateWishlistCommand request,
        CancellationToken cancellationToken)
    {
        var name = WishlistTextNormalizer.NormalizeName(request.Name ?? string.Empty);
        var normalizedName = WishlistTextNormalizer.NormalizeNameForUniqueness(name);
        var message = WishlistTextNormalizer.NormalizeMessage(request.Message);
        ApplicationLogMessages.WishlistUpdateStarted(
            logger,
            request.OwnerId,
            request.WishlistId);
        var wishlist = await wishlistService.UpdateAsync(
            request.OwnerId,
            request.WishlistId,
            name,
            normalizedName,
            request.Occasion ?? default,
            request.EventDate,
            message,
            request.ExpectedVersion,
            cancellationToken);

        if (wishlist is null)
            throw new WishlistNotFoundException();

        ApplicationLogMessages.WishlistUpdated(
            logger,
            request.OwnerId,
            request.WishlistId);

        return wishlist;
    }
}
