using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetGiftReservationHistoryQueryValidatorTests
{
    private readonly GetGiftReservationHistoryQueryValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenQueryIsValid_ReturnsNoErrors()
    {
        // Arrange
        var query = new GetGiftReservationHistoryQuery(
            Guid.CreateVersion7(),
            null,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, 20, null, "Page")]
    [InlineData(1, 0, null, "PageSize")]
    [InlineData(1, 101, null, "PageSize")]
    [InlineData(1, 20, "", "Status")]
    [InlineData(1, 20, "Active", "Status")]
    [InlineData(1, 20, "0", "Status")]
    [InlineData(1, 20, "unknown", "Status")]
    public async Task ValidateAsync_WhenQueryParameterIsInvalid_ReturnsExpectedError(
        int page,
        int pageSize,
        string? status,
        string expectedPropertyName)
    {
        // Arrange
        var query = new GetGiftReservationHistoryQuery(
            Guid.CreateVersion7(),
            page,
            pageSize,
            status);

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            expectedPropertyName,
            error.PropertyName);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberIdIsEmpty_ReturnsMemberIdError()
    {
        // Arrange
        var query = new GetGiftReservationHistoryQuery(
            Guid.Empty,
            1,
            20,
            "active");

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            "MemberId",
            error.PropertyName);
    }
}
