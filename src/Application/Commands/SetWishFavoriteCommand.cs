using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests a versioned owner favorite preference change.</summary>
/// <param name="ownerId">The authenticated owner identifier.</param>
/// <param name="wishlistId">The parent wishlist identifier.</param>
/// <param name="wishId">The wish identifier.</param>
/// <param name="isFavorite">The preference validated by the common pipeline.</param>
/// <param name="expectedVersion">The client wish version.</param>
public class SetWishFavoriteCommand(
    Guid ownerId,
    Guid wishlistId,
    Guid wishId,
    bool? isFavorite,
    uint expectedVersion) : IRequest<WishDetails>, IGenericValidationFailure
{
    /// <summary>Gets the authenticated owner identifier.</summary>
    public Guid OwnerId { get; } = ownerId;
    /// <summary>Gets the parent wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the wish identifier.</summary>
    public Guid WishId { get; } = wishId;
    /// <summary>Gets the requested preference.</summary>
    public bool? IsFavorite { get; } = isFavorite;
    /// <summary>Gets the client wish version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>Coordinates a preference-only mutation without changing wish order or content.</summary>
/// <param name="wishService">The transactional wish service.</param>
/// <param name="logger">The structured logger.</param>
public class SetWishFavoriteCommandHandler(
    IWishService wishService,
    ILogger<SetWishFavoriteCommandHandler> logger)
    : IRequestHandler<SetWishFavoriteCommand, WishDetails>
{
    /// <summary>Changes the owner preference validated by the pipeline.</summary>
    /// <param name="request">The preference command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated wish and concurrency version.</returns>
    /// <exception cref="WishNotFoundException">The wish does not exist under the owned parent.</exception>
    public async Task<WishDetails> Handle(
        SetWishFavoriteCommand request,
        CancellationToken cancellationToken)
    {
        var wish = await wishService.SetFavoriteAsync(
            request.OwnerId,
            request.WishlistId,
            request.WishId,
            request.IsFavorite.GetValueOrDefault(),
            request.ExpectedVersion,
            cancellationToken);

        if (wish is null)
            throw new WishNotFoundException();

        ApplicationLogMessages.WishFavoriteChanged(
            logger,
            request.OwnerId,
            request.WishlistId,
            request.WishId,
            wish.IsFavorite);

        return wish;
    }
}
