using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishesQueryValidatorTests
{
    private readonly GetWishesQueryValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenQueryIsValid_ReturnsSuccess()
    {
        // Arrange
        var query = new GetWishesQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAreEmpty_ReturnsIdentifierFailures()
    {
        // Arrange
        var query = new GetWishesQuery(
            Guid.Empty,
            Guid.Empty);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                nameof(query.OwnerId),
                nameof(query.WishlistId)
            ],
            result.Errors.Select(error => error.PropertyName));
    }
}
