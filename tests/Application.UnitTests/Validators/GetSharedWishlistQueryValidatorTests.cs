using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetSharedWishlistQueryValidatorTests
{
    private readonly GetSharedWishlistQueryValidator _validator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_WhenValuesVary_ReturnsExpectedResult(bool valuesAreMissing)
    {
        // Arrange
        var query = new GetSharedWishlistQuery(
            valuesAreMissing
                ? Guid.Empty
                : Guid.CreateVersion7(),
            valuesAreMissing
                ? null
                : "secret");

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            !valuesAreMissing,
            result.IsValid);
        Assert.Equal(
            valuesAreMissing
                ? 2
                : 0,
            result.Errors.Count);
    }
}
