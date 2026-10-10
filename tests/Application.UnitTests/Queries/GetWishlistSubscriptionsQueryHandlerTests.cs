using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistSubscriptionsQueryHandlerTests
{
    private readonly Mock<IWishlistSubscriptionService> _serviceMock;
    private readonly GetWishlistSubscriptionsQueryHandler _handler;

    public GetWishlistSubscriptionsQueryHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSubscriptionService>(MockBehavior.Strict);
        _handler = new GetWishlistSubscriptionsQueryHandler(
            _serviceMock.Object,
            NullLogger<GetWishlistSubscriptionsQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(2, 100, 2, 100)]
    public async Task Handle_WhenReadingPage_UsesDefaultsOnlyForAbsentValues(
        int? page,
        int? pageSize,
        int expectedPage,
        int expectedSize)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetWishlistSubscriptionsQuery(
            Guid.CreateVersion7(),
            page,
            pageSize);
        var expected = WishlistSubscriptionTestData.CreatePage();
        _serviceMock.Setup(service => service.GetPageAsync(
                request.MemberId,
                expectedPage,
                expectedSize,
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
        _serviceMock.Verify(service => service.GetPageAsync(
                request.MemberId,
                expectedPage,
                expectedSize,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
