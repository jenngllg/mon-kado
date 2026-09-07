using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishlistReportTests
{
    [Fact]
    public void Constructor_WhenValuesAreProvided_InitializesAnonymousReport()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();

        // Act
        var report = new WishlistReport(
            id,
            wishlistId,
            WishlistReportReason.PrivacyViolation,
            "Private information");

        // Assert
        Assert.Equal(
            id,
            report.Id);
        Assert.Equal(
            wishlistId,
            report.WishlistId);
        Assert.Equal(
            WishlistReportReason.PrivacyViolation,
            report.Reason);
        Assert.Equal(
            "Private information",
            report.Details);
        Assert.Equal(
            default,
            report.CreatedAt);
        Assert.Null(report.UpdatedAt);
        Assert.Equal(WishlistReportStatus.Pending, report.Status);
        Assert.Null(report.ReviewNote);
        Assert.Null(report.ReviewedAt);
        Assert.Null(report.ReviewedByAdministratorId);
        Assert.Equal(0u, report.Version);
    }

    [Theory]
    [InlineData(WishlistReportStatus.Upheld, null, null)]
    [InlineData(WishlistReportStatus.Dismissed, "  private note  ", "private note")]
    [InlineData(WishlistReportStatus.Pending, "annotation", "annotation")]
    [InlineData(WishlistReportStatus.Upheld, " \t\r\n ", null)]
    public void Review_WhenDispositionOrNoteChanges_UpdatesOnlyReviewMetadata(WishlistReportStatus status, string? input, string? normalized)
    {
        // Arrange
        var report = new WishlistReport(Guid.CreateVersion7(), Guid.CreateVersion7(), WishlistReportReason.Other, "Original visitor text");
        var administratorId = Guid.CreateVersion7();
        var date = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var changed = report.Review(status, input, administratorId, date);

        // Assert
        Assert.True(changed);
        Assert.Equal(status, report.Status);
        Assert.Equal(normalized, report.ReviewNote);
        Assert.Equal(date, report.ReviewedAt);
        Assert.Equal(administratorId, report.ReviewedByAdministratorId);
        Assert.Equal("Original visitor text", report.Details);
        Assert.Equal(WishlistReportReason.Other, report.Reason);
        Assert.Null(report.UpdatedAt);
    }

    [Theory]
    [InlineData(null, "  ")]
    [InlineData("note", " note ")]
    public void Review_WhenNormalizedDecisionIsUnchanged_PreservesOriginalReviewerAndDate(string? firstNote, string? repeatedNote)
    {
        // Arrange
        var report = new WishlistReport(Guid.CreateVersion7(), Guid.CreateVersion7(), WishlistReportReason.Other, null);
        var administratorId = Guid.CreateVersion7();
        var date = DateTime.UnixEpoch;
        report.Review(WishlistReportStatus.Upheld, firstNote, administratorId, date);

        // Act
        var changed = report.Review(WishlistReportStatus.Upheld, repeatedNote, Guid.CreateVersion7(), date.AddDays(1));

        // Assert
        Assert.False(changed);
        Assert.Equal(administratorId, report.ReviewedByAdministratorId);
        Assert.Equal(date, report.ReviewedAt);
    }
}
