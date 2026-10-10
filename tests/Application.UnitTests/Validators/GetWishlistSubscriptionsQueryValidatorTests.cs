using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistSubscriptionsQueryValidatorTests
{
    private readonly GetWishlistSubscriptionsQueryValidator _validator = new();

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, 1, true)]
    [InlineData(int.MaxValue, 100, true)]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    public void Validate_WhenPaginationIsProvided_RejectsOnlyOutOfRangeValues(
        int? page,
        int? pageSize,
        bool expected)
    {
        // Arrange
        var request = new GetWishlistSubscriptionsQuery(
            Guid.CreateVersion7(),
            page,
            pageSize);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }

    [Fact]
    public void Validate_WhenMemberIsMissing_RejectsRequest()
    {
        // Arrange
        var request = new GetWishlistSubscriptionsQuery(
            Guid.Empty,
            null,
            null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
    }
}
