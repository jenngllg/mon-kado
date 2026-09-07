using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistReportsQueryValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifiersOrFiltersAreInvalid_ReportsAllErrors(bool valid)
    {
        // Arrange
        var validator = new GetWishlistReportsQueryValidator();
        var request = new GetWishlistReportsQuery(
            valid ? Guid.CreateVersion7() : Guid.Empty,
            valid ? WishlistReportReason.Other : (WishlistReportReason)999,
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
            valid ? 0 : 4,
            result.Errors.Count);
    }

    [Fact]
    public async Task ValidateAsync_WhenOptionalFiltersAreAbsent_AcceptsDefaults()
    {
        // Arrange
        var validator = new GetWishlistReportsQueryValidator();
        var request = new GetWishlistReportsQuery(
            Guid.CreateVersion7(),
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
