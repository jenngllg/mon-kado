using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests a page of administrator-only moderation history.</summary>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="page">The optional one-based page number.</param>
/// <param name="pageSize">The optional page size.</param>
public class GetWishlistModerationEventsQuery(
    Guid wishlistId,
    int? page,
    int? pageSize) : IRequest<WishlistModerationEventPage>
{
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the incoming one-based page number.</summary>
    public int? Page { get; } = page;
    /// <summary>Gets the incoming page size.</summary>
    public int? PageSize { get; } = pageSize;
}

/// <summary>Coordinates administrator moderation-history reads.</summary>
/// <param name="service">The moderation service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistModerationEventsQueryHandler(
    IWishlistModerationService service,
    ILogger<GetWishlistModerationEventsQueryHandler> logger) : IRequestHandler<GetWishlistModerationEventsQuery, WishlistModerationEventPage>
{
    /// <summary>Reads a validated page of durable decisions.</summary>
    /// <param name="request">The validated query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page.</returns>
    public async Task<WishlistModerationEventPage> Handle(
        GetWishlistModerationEventsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEventsAsync(
            request.WishlistId,
            request.Page ?? WishlistModerationValidation.DefaultPage,
            request.PageSize ?? WishlistModerationValidation.DefaultPageSize,
            cancellationToken);
        WishlistModerationLogMessages.EventsRetrieved(
            logger,
            request.WishlistId);

        return result;
    }
}
