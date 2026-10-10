using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;

/// <summary>Provides a post-commit new-wish integration point without sending or retaining notifications.</summary>
/// <typeparam name="TRequest">The application request.</typeparam>
/// <typeparam name="TResponse">The application response.</typeparam>
/// <param name="publisher">The in-process integration publisher.</param>
public class WishAddedNotificationBehavior<TRequest, TResponse>(IPublisher publisher)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Publishes technical identifiers only after successful wish creation or copying.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="next">The committed handler.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The unchanged handler response.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (response is WishDetails wish &&
            request is CreateWishCommand or CopyWishCommand or CopyOwnedWishCommand)
        {
            await publisher.Publish(
                new WishAddedNotification(
                    wish.WishlistId,
                    wish.Id,
                    wish.CreatedAt),
                cancellationToken);
        }

        return response;
    }
}
