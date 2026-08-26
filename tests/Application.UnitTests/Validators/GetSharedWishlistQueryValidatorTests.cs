using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetSharedWishlistQueryValidatorTests
{
    private readonly GetSharedWishlistQueryValidator _validator = new();

    [Theory]
    [InlineData(false, false, true, 0)]
    [InlineData(true, false, false, 2)]
    [InlineData(false, true, false, 1)]
    public async Task ValidateAsync_WhenValuesVary_ReturnsExpectedResult(
        bool valuesAreMissing,
        bool memberIdIsEmpty,
        bool expectedIsValid,
        int expectedErrorCount)
    {
        // Arrange
        var query = new GetSharedWishlistQuery(
            valuesAreMissing
                ? Guid.Empty
                : Guid.CreateVersion7(),
            valuesAreMissing
                ? null
                : "secret",
            memberIdIsEmpty
                ? Guid.Empty
                : null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedIsValid,
            result.IsValid);
        Assert.Equal(
            expectedErrorCount,
            result.Errors.Count);
    }
}
