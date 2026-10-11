using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Represents the DeleteWishlistSubscriptionCommand operation.</summary>
/// <param name="memberId">The memberId.</param>
/// <param name="id">The id.</param>
public class DeleteWishlistSubscriptionCommand(
    Guid memberId,
    Guid id) : IRequest<Unit>
{
    /// <summary>Gets the memberId.</summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>Gets the id.</summary>
    public Guid Id { get; } = id;
}

/// <summary>Handles the subscription operation without affecting gift participation.</summary>
/// <param name="service">The subscription service.</param>
/// <param name="logger">The structured logger.</param>
public class DeleteWishlistSubscriptionCommandHandler(
    IWishlistSubscriptionService service,
    ILogger<DeleteWishlistSubscriptionCommandHandler> logger) : IRequestHandler<DeleteWishlistSubscriptionCommand, Unit>
{
    /// <summary>Executes the validated subscription operation.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<Unit> Handle(
        DeleteWishlistSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var removed = await service.DeleteAsync(
            request.MemberId,
            request.Id,
            cancellationToken);

        if (!removed)
            throw new WishlistSubscriptionNotFoundException();

        WishlistSubscriptionLogMessages.Removed(
            logger,
            request.MemberId,
            request.Id);

        return Unit.Value;
    }
}
