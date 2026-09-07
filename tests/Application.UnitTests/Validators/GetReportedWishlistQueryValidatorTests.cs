using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetReportedWishlistQueryValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifiersOrFiltersAreInvalid_ReportsAllErrors(bool valid)
    {
        // Arrange
        var validator = new GetReportedWishlistQueryValidator();
        var request = new GetReportedWishlistQuery(valid ? Guid.CreateVersion7() : Guid.Empty);

        // Act
        var result = await validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
        Assert.Equal(
            valid ? 0 : 1,
            result.Errors.Count);
    }
}
