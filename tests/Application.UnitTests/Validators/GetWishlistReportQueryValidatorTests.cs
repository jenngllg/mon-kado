using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistReportQueryValidatorTests
{
    private readonly GetWishlistReportQueryValidator _validator = new();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_WhenIdentifiersAreValidated_RequiresBothParentAndReport(bool valid)
    {
        // Arrange
        var id = valid ? Guid.CreateVersion7() : Guid.Empty;
        var request = new GetWishlistReportQuery(
            id,
            id);

        // Act
        var result = await _validator.ValidateAsync(
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
