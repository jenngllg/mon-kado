using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishQueryValidatorTests
{
    private readonly GetWishQueryValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAreValid_ReturnsSuccess()
    {
        // Arrange
        var query = new GetWishQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(nameof(GetWishQuery.MemberId))]
    [InlineData(nameof(GetWishQuery.WishlistId))]
    [InlineData(nameof(GetWishQuery.WishId))]
    public async Task ValidateAsync_WhenIdentifierIsEmpty_ReturnsIdentifierFailure(string propertyName)
    {
        // Arrange
        var query = new GetWishQuery(
            propertyName == nameof(GetWishQuery.MemberId)
                ? Guid.Empty
                : Guid.CreateVersion7(),
            propertyName == nameof(GetWishQuery.WishlistId)
                ? Guid.Empty
                : Guid.CreateVersion7(),
            propertyName == nameof(GetWishQuery.WishId)
                ? Guid.Empty
                : Guid.CreateVersion7());

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == propertyName);
    }
}
