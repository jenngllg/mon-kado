using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetReportedWishImageQueryValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifiersOrFiltersAreInvalid_ReportsAllErrors(bool valid)
    {
        // Arrange
        var validator = new GetReportedWishImageQueryValidator();
        var request = new GetReportedWishImageQuery(
            valid ? Guid.CreateVersion7() : Guid.Empty,
            valid ? Guid.CreateVersion7() : Guid.Empty);

        // Act
        var result = await validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
        Assert.Equal(
            valid ? 0 : 2,
            result.Errors.Count);
    }
}
