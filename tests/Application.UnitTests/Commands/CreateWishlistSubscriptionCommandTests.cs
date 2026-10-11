using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishlistSubscriptionCommandTests
{
    [Fact]
    public async Task Validation_WhenCapabilityIsMissing_UsesGenericNotFoundFailure()
    {
        // Arrange
        var request = new CreateWishlistSubscriptionCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null);
        var behavior = new ValidationBehavior<CreateWishlistSubscriptionCommand, WishlistSubscriptionDetails>(
            [new CreateWishlistSubscriptionCommandValidator()]);

        // Act
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(() => behavior.Handle(
            request,
            _ => throw new InvalidOperationException("Invalid requests must not reach the handler."),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(request.Secret);
    }
}
