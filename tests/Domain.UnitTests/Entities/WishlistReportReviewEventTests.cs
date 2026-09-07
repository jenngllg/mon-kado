using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishlistReportReviewEventTests
{
    [Fact]
    public void Constructor_WhenDecisionIsProvided_RetainsImmutableReviewData()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var reportId = Guid.CreateVersion7();
        var administratorId = Guid.CreateVersion7();
        var report = new WishlistReport(
            reportId,
            Guid.CreateVersion7(),
            WishlistReportReason.Other,
            null);
        report.Review(
            WishlistReportStatus.Dismissed,
            "Corrected decision",
            administratorId,
            DateTime.UnixEpoch);

        // Act
        var review = new WishlistReportReviewEvent(
            id,
            report,
            2,
            WishlistReportStatus.Upheld);
        report.Review(
            WishlistReportStatus.Pending,
            null,
            Guid.CreateVersion7(),
            DateTime.UnixEpoch.AddDays(1));

        // Assert
        Assert.Equal(
            id,
            review.Id);
        Assert.Equal(
            reportId,
            review.ReportId);
        Assert.Equal(
            2,
            review.Sequence);
        Assert.Equal(
            WishlistReportStatus.Upheld,
            review.PreviousStatus);
        Assert.Equal(
            WishlistReportStatus.Dismissed,
            review.Status);
        Assert.Equal(
            "Corrected decision",
            review.Note);
        Assert.Equal(
            administratorId,
            review.AdministratorId);
        Assert.Equal(
            DateTime.UnixEpoch,
            review.OccurredAt);
    }

    [Fact]
    public void Constructor_WhenReportWasNeverReviewed_RejectsFabricatedHistory()
    {
        // Arrange
        var report = new WishlistReport(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WishlistReportReason.Other,
            null);

        // Act
        var exception = Record.Exception(() => new WishlistReportReviewEvent(
            Guid.CreateVersion7(),
            report,
            1,
            WishlistReportStatus.Pending));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }
}
