using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Represents the GetWishlistSubscriptionQuery operation.</summary>
/// <param name="memberId">The memberId.</param>
/// <param name="id">The id.</param>
public class GetWishlistSubscriptionQuery(
    Guid memberId,
    Guid id) : IRequest<WishlistSubscriptionDetails>
{
    /// <summary>Gets the memberId.</summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>Gets the id.</summary>
    public Guid Id { get; } = id;
}

/// <summary>Handles the subscription operation without affecting gift participation.</summary>
/// <param name="service">The subscription service.</param>
/// <param name="logger">The structured logger.</param>
public class GetWishlistSubscriptionQueryHandler(
    IWishlistSubscriptionService service,
    ILogger<GetWishlistSubscriptionQueryHandler> logger) : IRequestHandler<GetWishlistSubscriptionQuery, WishlistSubscriptionDetails>
{
    /// <summary>Executes the validated subscription operation.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<WishlistSubscriptionDetails> Handle(
        GetWishlistSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(
            request.MemberId,
            request.Id,
            cancellationToken) ?? throw new WishlistSubscriptionNotFoundException();
        WishlistSubscriptionLogMessages.Read(
            logger,
            request.MemberId,
            result.Id);

        return result;
    }
}
