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

        // Act
        var review = new WishlistReportReviewEvent(
            id,
            reportId,
            2,
            WishlistReportStatus.Upheld,
            WishlistReportStatus.Dismissed,
            "Corrected decision",
            administratorId,
            DateTime.UnixEpoch);

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
}
