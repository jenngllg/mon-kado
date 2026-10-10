using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishlistSubscriptionCommandHandlerTests
{
    private readonly Mock<IWishlistSubscriptionService> _serviceMock;
    private readonly CreateWishlistSubscriptionCommandHandler _handler;

    public CreateWishlistSubscriptionCommandHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSubscriptionService>(MockBehavior.Strict);
        _handler = new CreateWishlistSubscriptionCommandHandler(
            _serviceMock.Object,
            NullLogger<CreateWishlistSubscriptionCommandHandler>.Instance);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData(null)]
    public async Task Handle_WhenServiceResponds_ForwardsCancellationAndPreservesResult(string? secret)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var request = new CreateWishlistSubscriptionCommand(
            memberId,
            id,
            secret);
        var expected = WishlistSubscriptionTestData.CreateDetails();
        _serviceMock.Setup(service => service.CreateAsync(
            memberId,
            id,
            secret ?? string.Empty,
            cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            request,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);

        // Assert
        _serviceMock.Verify(service => service.CreateAsync(
            memberId,
            id,
            secret ?? string.Empty,
            cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
