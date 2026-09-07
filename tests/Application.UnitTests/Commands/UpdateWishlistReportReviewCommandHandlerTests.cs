using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateWishlistReportReviewCommandHandlerTests
{
    private readonly Mock<IWishlistReportReviewService> _serviceMock = new(MockBehavior.Strict);
    private readonly UpdateWishlistReportReviewCommandHandler _handler;
    public UpdateWishlistReportReviewCommandHandlerTests()
    {
        _handler = new UpdateWishlistReportReviewCommandHandler(
            _serviceMock.Object,
            NullLogger<UpdateWishlistReportReviewCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new UpdateWishlistReportReviewCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WishlistReportStatus.Upheld,
            "Private note",
            42);
        var result = TestFixture
            .Create()
            .Create<VersionedWishlistReportDetails>();
        _serviceMock
            .Setup(service => service.UpdateAsync(
                request.AdministratorId,
                request.WishlistId,
                request.ReportId,
                WishlistReportStatus.Upheld,
                request.ReviewNote,
                request.ExpectedVersion,
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
            service => service.UpdateAsync(
                request.AdministratorId,
                request.WishlistId,
                request.ReportId,
                WishlistReportStatus.Upheld,
                request.ReviewNote,
                request.ExpectedVersion,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
