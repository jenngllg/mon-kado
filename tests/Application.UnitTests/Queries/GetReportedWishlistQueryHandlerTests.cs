using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetReportedWishlistQueryHandlerTests
{
    private readonly Mock<IReportedWishlistService> _serviceMock = new(MockBehavior.Strict);
    private readonly GetReportedWishlistQueryHandler _handler;
    public GetReportedWishlistQueryHandlerTests()
    {
        _handler = new GetReportedWishlistQueryHandler(
            _serviceMock.Object,
            NullLogger<GetReportedWishlistQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenReadSucceeds_ReturnsResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetReportedWishlistQuery(Guid.CreateVersion7());
        var fixture = TestFixture.Create();
        fixture.Register<DateOnly?>(() => null);
        var result = fixture.Create<ReportedWishlistDetails>();
        _serviceMock
            .Setup(service => service.GetAsync(
                request.WishlistId,
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
            service => service.GetAsync(
                request.WishlistId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
