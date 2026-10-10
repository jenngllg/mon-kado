using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using MediatR;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common.Behaviors;

public class WishAddedNotificationBehaviorTests
{
    private readonly Mock<IPublisher> _publisherMock;
    private readonly WishAddedNotificationBehavior<object, object> _behavior;

    public WishAddedNotificationBehaviorTests()
    {
        _publisherMock = new Mock<IPublisher>(MockBehavior.Strict);
        _behavior = new WishAddedNotificationBehavior<object, object>(_publisherMock.Object);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("shared-copy")]
    [InlineData("owned-copy")]
    [InlineData("other-request")]
    [InlineData("other-response")]
    public async Task Handle_WhenHandlerCommits_PublishesOnlyNewWishIdentifiers(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = TestFixture.Create();
        object request = fixture.Create<CreateWishCommand>();

        if (scenario == "shared-copy")
            request = fixture.Create<CopyWishCommand>();

        if (scenario == "owned-copy")
            request = fixture.Create<CopyOwnedWishCommand>();

        if (scenario == "other-request")
            request = new object();

        var wish = fixture.Create<WishDetails>();
        object response = scenario == "other-response" ? new object() : wish;
        var expected = scenario is not ("other-request" or "other-response");
        var handlerCompleted = false;

        if (expected)
            _publisherMock.Setup(publisher => publisher.Publish(
                    It.Is<WishAddedNotification>(notification =>
                        handlerCompleted &&
                        notification.WishlistId == wish.WishlistId &&
                        notification.WishId == wish.Id &&
                        notification.OccurredAt == wish.CreatedAt),
                    cancellationToken))
                .Returns(Task.CompletedTask);

        // Act
        var result = await _behavior.Handle(
            request,
            token =>
            {
                Assert.Equal(
                    cancellationToken,
                    token);
                handlerCompleted = true;

                return Task.FromResult(response);
            },
            cancellationToken);

        // Assert
        Assert.Same(
            response,
            result);

        if (expected)
            _publisherMock.Verify(publisher => publisher.Publish(
                    It.Is<WishAddedNotification>(notification =>
                        notification.WishlistId == wish.WishlistId &&
                        notification.WishId == wish.Id &&
                        notification.OccurredAt == wish.CreatedAt),
                    cancellationToken),
                Times.Once);

        _publisherMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenHandlerFails_DoesNotPublishNotification()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = TestFixture.Create().Create<CreateWishCommand>();

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => _behavior.Handle(
            request,
            _ => throw new InvalidOperationException(),
            cancellationToken));

        // Assert
        _publisherMock.VerifyNoOtherCalls();
    }
}
