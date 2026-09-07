using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetWishlistReportReviewEventsQueryValidatorTests
{
    private readonly GetWishlistReportReviewEventsQueryValidator _validator = new();
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, 100, true)]
    [InlineData(0, 0, false)]
    [InlineData(1, 101, false)]
    public async Task ValidateAsync_WhenPaginationIsProvided_AppliesOneBasedBoundaries(
        int? page,
        int? pageSize,
        bool valid)
    {
        // Arrange
        var request = new GetWishlistReportReviewEventsQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            page,
            pageSize);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAreMissing_AggregatesBothFailures()
    {
        // Arrange
        var request = new GetWishlistReportReviewEventsQuery(
            Guid.Empty,
            Guid.Empty,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                "ReportId",
                "WishlistId"
            ],
            result.Errors
                .Select(error => error.PropertyName)
                .Order());
    }
}
