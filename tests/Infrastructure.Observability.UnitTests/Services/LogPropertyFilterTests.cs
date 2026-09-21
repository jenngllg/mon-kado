using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Services;

public class LogPropertyFilterTests
{
    private readonly LogPropertyFilter _filter = new(["ACCOUNT_NOT_FOUND"]);

    [Theory]
    [InlineData("Count", 3)]
    [InlineData("Count", 3L)]
    [InlineData("StatusCode", 503)]
    [InlineData("ElapsedMilliseconds", 2.5)]
    [InlineData("Abandoned", true)]
    [InlineData("IsReferenced", false)]
    [InlineData("Method", "GET")]
    [InlineData("RoutePattern", "api/v1/wishlists/{wishlistId:guid}")]
    [InlineData("Classification", "LEASE_LOST")]
    [InlineData("ErrorCode", "ACCOUNT_NOT_FOUND")]
    public void Filter_WhenTypedTechnicalValue_ReturnsValue(
        string name,
        object value)
    {
        // Arrange / Act
        var result = _filter.Filter(
            name,
            value);

        // Assert
        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("Password", "secret")]
    [InlineData("Body", "secret")]
    [InlineData("UserId", "secret")]
    [InlineData("Count", "secret")]
    [InlineData("Count", -1)]
    [InlineData("Count", -1L)]
    [InlineData("Abandoned", "secret")]
    [InlineData("ElapsedMilliseconds", double.NaN)]
    [InlineData("ElapsedMilliseconds", "secret")]
    [InlineData("ElapsedMilliseconds", -1.0)]
    [InlineData("Category", "secret")]
    [InlineData("FailureCategory", "secret")]
    [InlineData("Method", "SECRET")]
    [InlineData("ErrorCode", "PRIVATE_TOKEN")]
    [InlineData("Classification", "PRIVATE_TOKEN")]
    [InlineData("ExceptionType", "secret")]
    [InlineData("RoutePattern", "/?token=secret")]
    [InlineData("Unknown", 1)]
    public void Filter_WhenUntrustedValue_DropsValue(
        string name,
        object value)
    {
        // Arrange / Act
        var result = _filter.Filter(
            name,
            value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Filter_WhenGuidOrKnownEnum_RetainsTypedIdentifiersOnly()
    {
        // Arrange
        var identifier = Guid.CreateVersion7();

        // Act / Assert
        Assert.Equal(identifier, _filter.Filter("UserId", identifier));
        Assert.Equal("Transient", _filter.Filter("Failure", AuthenticationEmailFailureCategory.Transient));
        Assert.Null(_filter.Filter("Failure", (AuthenticationEmailFailureCategory)999));
        Assert.Null(_filter.Filter("Failure", DayOfWeek.Monday));
        Assert.Null(_filter.Filter("RoutePattern", new string('a', 201)));
    }
}
