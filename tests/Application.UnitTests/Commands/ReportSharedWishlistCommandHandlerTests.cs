using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ReportSharedWishlistCommandHandlerTests
{
    private readonly ReportSharedWishlistCommandHandler _handler;
    private readonly Mock<IWishlistReportService> _reportServiceMock;

    public ReportSharedWishlistCommandHandlerTests()
    {
        _reportServiceMock = new Mock<IWishlistReportService>(MockBehavior.Strict);
        _handler = new ReportSharedWishlistCommandHandler(
            _reportServiceMock.Object,
            NullLogger<ReportSharedWishlistCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenRequestIsValid_CreatesNormalizedAnonymousReport()
    {
        // Arrange
        var command = new ReportSharedWishlistCommand(
            Guid.CreateVersion7(),
            "secret",
            WishlistReportReason.Other,
            "  De\u0301tails  ");
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _reportServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(reportId => reportId.Version == 7),
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                WishlistReportReason.Other,
                "Détails",
                cancellationToken))
            .ReturnsAsync(wishlistId);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _reportServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(reportId => reportId.Version == 7),
                command.ShareLinkId,
                command.Secret ?? string.Empty,
                WishlistReportReason.Other,
                "Détails",
                cancellationToken),
            Times.Once);
        _reportServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenNullableValuesAreMissing_UsesFallbackValues()
    {
        // Arrange
        var command = new ReportSharedWishlistCommand(
            Guid.CreateVersion7(),
            null,
            null,
            null);
        var cancellationToken = TestContext.Current.CancellationToken;
        _reportServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<Guid>(reportId => reportId.Version == 7),
                command.ShareLinkId,
                string.Empty,
                default,
                null,
                cancellationToken))
            .ReturnsAsync(Guid.CreateVersion7());

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _reportServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<Guid>(reportId => reportId.Version == 7),
                command.ShareLinkId,
                string.Empty,
                default,
                null,
                cancellationToken),
            Times.Once);
        _reportServiceMock.VerifyNoOtherCalls();
    }
}
