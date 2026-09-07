using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetReportedWishlistsQueryValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifiersOrFiltersAreInvalid_ReportsAllErrors(bool valid)
    {
        // Arrange
        var validator = new GetReportedWishlistsQueryValidator();
        var request = new GetReportedWishlistsQuery(
            valid ? WishlistReportReason.Other : (WishlistReportReason)999,
            null,
            valid ? 1 : 0,
            valid ? 20 : 101);

        // Act
        var result = await validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
        Assert.Equal(
            valid ? 0 : 3,
            result.Errors.Count);
    }

    [Fact]
    public async Task ValidateAsync_WhenOptionalFiltersAreAbsent_AcceptsDefaults()
    {
        // Arrange
        var validator = new GetReportedWishlistsQueryValidator();
        var request = new GetReportedWishlistsQuery(
            null,
            null,
            null,
            null);

        // Act
        var result = await validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }
}
