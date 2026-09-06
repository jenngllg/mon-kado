using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class SearchUsersQueryValidatorTests
{
    private readonly SearchUsersQueryValidator _validator = new();
    [Theory]
    [InlineData(
        80,
        true)]
    [InlineData(
        81,
        false)]
    public async Task ValidateAsync_WhenUnicodeLengthIsAtBoundary_CountsScalarsRatherThanUtf16Units(
        int length,
        bool valid)
    {
        // Arrange
        var term = string.Concat(Enumerable.Repeat(
                "😀",
                length));
        var query = new SearchUsersQuery(
            term,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
    }

    [Theory]
    [InlineData("e\u0301")]
    [InlineData("\tJen")]
    public async Task ValidateAsync_WhenTermIsMalformedOrTooShort_RejectsTerm(string term)
    {
        // Arrange
        var query = new SearchUsersQuery(
            term,
            1,
            20);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.All(
            result.Errors,
            error => Assert.Equal(
                nameof(SearchUsersQuery.DisplayName),
                error.PropertyName));
    }

    [Fact]
    public async Task ValidateAsync_WhenUtf16ContainsUnpairedSurrogate_RejectsTerm()
    {
        // Arrange
        var term = new string(
            (char)0xD800,
            2);
        var query = new SearchUsersQuery(
            term,
            1,
            20);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
    }
}
