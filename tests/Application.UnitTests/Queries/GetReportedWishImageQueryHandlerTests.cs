using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetReportedWishImageQueryHandlerTests
{
    private readonly Mock<IReportedWishlistService> _serviceMock = new(MockBehavior.Strict);
    private readonly GetReportedWishImageQueryHandler _handler;
    public GetReportedWishImageQueryHandlerTests()
    {
        _handler = new GetReportedWishImageQueryHandler(
            _serviceMock.Object,
            NullLogger<GetReportedWishImageQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenReadSucceeds_ReturnsResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetReportedWishImageQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        using var result = new MemoryStream();
        _serviceMock
            .Setup(service => service.OpenImageAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken))
            .ReturnsAsync(result);

        // Act
        var actual = await _handler.Handle(
            request,
            cancellationToken);

        // Assert
        Assert.Same(
            result,
            actual);
        _serviceMock.Verify(
            service => service.OpenImageAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
