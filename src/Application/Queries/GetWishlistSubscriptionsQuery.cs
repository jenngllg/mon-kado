using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Represents the GetWishlistSubscriptionsQuery operation.</summary>
/// <param name="memberId">The memberId.</param>
/// <param name="page">The page.</param>
/// <param name="pageSize">The pageSize.</param>
public class GetWishlistSubscriptionsQuery(
    Guid memberId,
    int? page,
    int? pageSize) : IRequest<WishlistSubscriptionPage>
{
    /// <summary>Gets the default page number.</summary>
    public const int DefaultPage = 1;
    /// <summary>Gets the default page size.</summary>
    public const int DefaultPageSize = 20;
    /// <summary>Gets the maximum page size.</summary>
    public const int MaximumPageSize = 100;

    /// <summary>Gets the memberId.</summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>Gets the page.</summary>
    public int? Page { get; } = page;

    /// <summary>Gets the pageSize.</summary>
    public int? PageSize { get; } = pageSize;
}

/// <summary>Handles the subscription operation without affecting gift participation.</summary>
/// <param name="service">The subscription service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistSubscriptionsQueryHandler(
    IWishlistSubscriptionService service,
    ILogger<GetWishlistSubscriptionsQueryHandler> logger) : IRequestHandler<GetWishlistSubscriptionsQuery, WishlistSubscriptionPage>
{
    /// <summary>Executes the validated subscription operation.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<WishlistSubscriptionPage> Handle(
        GetWishlistSubscriptionsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPageAsync(
            request.MemberId,
            request.Page ?? GetWishlistSubscriptionsQuery.DefaultPage,
            request.PageSize ?? GetWishlistSubscriptionsQuery.DefaultPageSize,
            cancellationToken);
        WishlistSubscriptionLogMessages.PageRead(
            logger,
            request.MemberId,
            result.TotalCount);

        return result;
    }
}
