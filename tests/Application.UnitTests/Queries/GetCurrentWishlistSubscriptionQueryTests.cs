using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentWishlistSubscriptionQueryTests
{
    [Fact]
    public async Task Validation_WhenCapabilityIsMissing_UsesGenericNotFoundFailure()
    {
        // Arrange
        var request = new GetCurrentWishlistSubscriptionQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null);
        var behavior = new ValidationBehavior<GetCurrentWishlistSubscriptionQuery, WishlistSubscriptionDetails>(
            [new GetCurrentWishlistSubscriptionQueryValidator()]);

        // Act
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(() => behavior.Handle(
            request,
            _ => throw new InvalidOperationException("Invalid requests must not reach the handler."),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(request.Secret);
    }
}
