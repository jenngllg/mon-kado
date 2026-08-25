using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistsQueryValidatorTests
{
    private readonly GetWishlistsQueryValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenMemberIdIsValid_ReturnsSuccess()
    {
        // Arrange
        var query = new GetWishlistsQuery(Guid.CreateVersion7());

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberIdIsEmpty_ReturnsMemberIdFailure()
    {
        // Arrange
        var query = new GetWishlistsQuery(Guid.Empty);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(query.MemberId));
    }
}
