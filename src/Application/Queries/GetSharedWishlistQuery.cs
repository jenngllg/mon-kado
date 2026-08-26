using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>
/// Represents public wishlist retrieval through a bearer share link.
/// </summary>
/// <param name="shareLinkId">The share-link identifier.</param>
/// <param name="secret">The nullable bearer secret received from the client.</param>
public class GetSharedWishlistQuery(
    Guid shareLinkId,
    string? secret) : IRequest<SharedWishlistDetails>, IGenericValidationFailure
{
    /// <summary>Gets the share-link identifier.</summary>
    public Guid ShareLinkId { get; } = shareLinkId;
    /// <summary>Gets the nullable bearer secret received from the client.</summary>
    public string? Secret { get; } = secret;

    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new SharedWishlistNotFoundException();
    }
}

/// <summary>
/// Handles public wishlist retrieval through a bearer share link.
/// </summary>
/// <param name="shareService">The wishlist share-link service.</param>
/// <param name="logger">The logger.</param>
public class GetSharedWishlistQueryHandler(
    IWishlistShareService shareService,
    ILogger<GetSharedWishlistQueryHandler> logger)
    : IRequestHandler<GetSharedWishlistQuery, SharedWishlistDetails>
{
    /// <summary>Gets the publicly shared wishlist.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public wishlist content.</returns>
    /// <exception cref="SharedWishlistNotFoundException">The share link is invalid or unavailable.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<SharedWishlistDetails> Handle(
        GetSharedWishlistQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.SharedWishlistRetrievalStarted(
            logger,
            request.ShareLinkId);
        var wishlist = await shareService.GetSharedAsync(
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken) ?? throw new SharedWishlistNotFoundException();
        ApplicationLogMessages.SharedWishlistRetrieved(
            logger,
            request.ShareLinkId,
            wishlist.Id);

        return wishlist;
    }
}
