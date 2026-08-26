using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistShareLinkQueryValidatorTests
{
    private readonly GetWishlistShareLinkQueryValidator _validator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_WhenIdentifiersVary_ReturnsExpectedResult(bool identifiersAreEmpty)
    {
        // Arrange
        var query = new GetWishlistShareLinkQuery(
            identifiersAreEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            identifiersAreEmpty
                ? Guid.Empty
                : Guid.CreateVersion7());

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            !identifiersAreEmpty,
            result.IsValid);
        Assert.Equal(
            identifiersAreEmpty
                ? 2
                : 0,
            result.Errors.Count);
    }
}
