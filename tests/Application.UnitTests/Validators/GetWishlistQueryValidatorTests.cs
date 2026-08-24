using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistQueryValidatorTests
{
    private readonly GetWishlistQueryValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAreValid_ReturnsSuccess()
    {
        // Arrange
        var query = new GetWishlistQuery(
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
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifierIsEmpty_ReturnsIdentifierFailure(bool memberIsEmpty)
    {
        // Arrange
        var query = new GetWishlistQuery(
            memberIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            memberIsEmpty
                ? Guid.CreateVersion7()
                : Guid.Empty);
        var expectedProperty = memberIsEmpty
            ? nameof(query.MemberId)
            : nameof(query.WishlistId);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == expectedProperty);
    }
}
