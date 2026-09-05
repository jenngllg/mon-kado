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
    }
}
