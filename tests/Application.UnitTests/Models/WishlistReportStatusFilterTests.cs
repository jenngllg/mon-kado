using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Models;

public class WishlistReportStatusFilterTests
{
    [Theory]
    [InlineData(WishlistReportStatusFilter.Pending, WishlistReportStatus.Pending)]
    [InlineData(WishlistReportStatusFilter.Upheld, WishlistReportStatus.Upheld)]
    [InlineData(WishlistReportStatusFilter.Dismissed, WishlistReportStatus.Dismissed)]
    public void Convert_WhenFilterSelectsOneState_MatchesThePersistedDisposition(
        WishlistReportStatusFilter filter,
        WishlistReportStatus expected)
    {
        // Arrange
        var value = filter;

        // Act
        var actual = (WishlistReportStatus)value;

        // Assert
        Assert.Equal(
            expected,
            actual);
        Assert.NotEqual(
            (int)WishlistReportStatusFilter.All,
            (int)actual);
    }
}
