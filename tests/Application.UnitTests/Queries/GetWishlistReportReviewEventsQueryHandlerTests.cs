using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistReportReviewEventsQueryHandlerTests
{
    private readonly Mock<IWishlistReportReviewService> _serviceMock = new(MockBehavior.Strict);
    private readonly GetWishlistReportReviewEventsQueryHandler _handler;
    public GetWishlistReportReviewEventsQueryHandlerTests()
    {
        _handler = new GetWishlistReportReviewEventsQueryHandler(
            _serviceMock.Object,
            NullLogger<GetWishlistReportReviewEventsQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(2, 7)]
    public async Task Handle_WhenServiceSucceeds_ReturnsResultAndForwardsCancellation(
        int? page,
        int? pageSize)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetWishlistReportReviewEventsQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            page,
            pageSize);
        var result = TestFixture
            .Create()
            .Create<WishlistReportReviewEventPage>();
        _serviceMock
            .Setup(service => service.GetEventsAsync(
                request.WishlistId,
                request.ReportId,
                page ?? 1,
                pageSize ?? 20,
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
            service => service.GetEventsAsync(
                request.WishlistId,
                request.ReportId,
                page ?? 1,
                pageSize ?? 20,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
