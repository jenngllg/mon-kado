using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetReportedWishlistsQueryHandlerTests
{
    private readonly Mock<IReportedWishlistService> _serviceMock = new(MockBehavior.Strict);
    private readonly GetReportedWishlistsQueryHandler _handler;
    public GetReportedWishlistsQueryHandlerTests()
    {
        _handler = new GetReportedWishlistsQueryHandler(
            _serviceMock.Object,
            NullLogger<GetReportedWishlistsQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(2, 7)]
    public async Task Handle_WhenReadSucceeds_ReturnsResultAndForwardsCancellation(
        int? page,
        int? pageSize)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new GetReportedWishlistsQuery(
            WishlistReportReason.Other,
            true,
            page,
            pageSize);
        var result = TestFixture
            .Create()
            .Create<ReportedWishlistPage>();
        _serviceMock
            .Setup(service => service.GetPageAsync(
                request.Reason,
                request.IsSuspended,
                request.Page ?? 1,
                request.PageSize ?? 20,
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
            service => service.GetPageAsync(
                request.Reason,
                request.IsSuspended,
                request.Page ?? 1,
                request.PageSize ?? 20,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
