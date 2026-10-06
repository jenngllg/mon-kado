using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistSharePreviewQueryValidatorTests
{
    private readonly GetWishlistSharePreviewQueryValidator _validator = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifierVaries_ReturnsExpectedValidity(bool empty)
    {
        // Arrange
        var query = new GetWishlistSharePreviewQuery(empty ? Guid.Empty : Guid.CreateVersion7());

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            !empty,
            result.IsValid);
        Assert.Equal(
            empty ? 1 : 0,
            result.Errors.Count);
    }
}
