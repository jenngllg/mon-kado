using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Contracts.Responses;

public class PaginatedResponseTests
{
    [Fact]
    public void Constructor_WhenNoItemsExist_ReturnsEmptyPaginationMetadata()
    {
        // Arrange
        var items = Array.Empty<string>();

        // Act
        var response = new PaginatedResponse<string>(
            items,
            1,
            20,
            0);

        // Assert
        Assert.Same(
            items,
            response.Items);
        Assert.Equal(
            1,
            response.CurrentPage);
        Assert.Equal(
            20,
            response.PageSize);
        Assert.Equal(
            0,
            response.TotalCount);
        Assert.Equal(
            0,
            response.TotalPages);
        Assert.False(response.HasPreviousPage);
        Assert.False(response.HasNextPage);
    }

    [Theory]
    [InlineData(1, 21, 3, false, true)]
    [InlineData(2, 21, 3, true, true)]
    [InlineData(3, 21, 3, true, false)]
    [InlineData(4, 21, 3, true, false)]
    [InlineData(1, int.MaxValue, 214748365, false, true)]
    public void Constructor_WhenItemsExist_ComputesPaginationMetadata(
        int currentPage,
        int totalCount,
        int expectedTotalPages,
        bool expectedHasPreviousPage,
        bool expectedHasNextPage)
    {
        // Arrange
        var items = Array.Empty<string>();

        // Act
        var response = new PaginatedResponse<string>(
            items,
            currentPage,
            10,
            totalCount);

        // Assert
        Assert.Equal(
            expectedTotalPages,
            response.TotalPages);
        Assert.Equal(
            expectedHasPreviousPage,
            response.HasPreviousPage);
        Assert.Equal(
            expectedHasNextPage,
            response.HasNextPage);
    }
}
