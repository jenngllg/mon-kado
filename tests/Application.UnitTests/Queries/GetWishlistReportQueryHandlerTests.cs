using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistReportQueryHandlerTests
{
    private readonly Mock<IWishlistReportReviewService> _serviceMock = new(MockBehavior.Strict);
    private readonly GetWishlistReportQueryHandler _handler;
    public GetWishlistReportQueryHandlerTests()
    {
        _handler = new GetWishlistReportQueryHandler(
            _serviceMock.Object,
            NullLogger<GetWishlistReportQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetWishlistReportQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        var result = TestFixture
            .Create()
            .Create<VersionedWishlistReportDetails>();
        _serviceMock
            .Setup(service => service.GetAsync(
                request.WishlistId,
                request.ReportId,
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
                request.ReportId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
