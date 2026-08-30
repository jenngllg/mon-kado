using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class EntityTagServiceTests
{
    private readonly EntityTagService _entityTagService = new();

    [Theory]
    [InlineData(0u, "\"00000000\"")]
    [InlineData(42u, "\"0000002a\"")]
    [InlineData(uint.MaxValue, "\"ffffffff\"")]
    public void Format_WhenVersionIsProvided_ReturnsStrongEntityTag(
        uint version,
        string expected)
    {
        // Arrange

        // Act
        var result = _entityTagService.Format(version);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData("\"00000000\"", 0u)]
    [InlineData("\"0000002a\"", 42u)]
    [InlineData("\"ffffffff\"", uint.MaxValue)]
    public void Parse_WhenEntityTagIsValid_ReturnsVersion(
        string entityTag,
        uint expected)
    {
        // Arrange

        // Act
        var result = _entityTagService.Parse(entityTag);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void Parse_WhenEntityTagIsMissing_ThrowsPreconditionRequiredException()
    {
        // Arrange

        // Act
        var action = () =>
        {
            _ = _entityTagService.Parse(null);
        };

        // Assert
        Assert.Throws<PreconditionRequiredException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0000002a")]
    [InlineData("\"0000002a")]
    [InlineData("0000002a\"")]
    [InlineData("X0000002a\"")]
    [InlineData("\"0000002aX")]
    [InlineData("\"0000002A\"")]
    [InlineData("\"not-hex!\"")]
    [InlineData("W/\"0000002a\"")]
    public void Parse_WhenEntityTagIsMalformed_ThrowsRequestValidationException(
        string entityTag)
    {
        // Arrange

        // Act
        var action = () =>
        {
            _ = _entityTagService.Parse(entityTag);
        };

        // Assert
        var exception = Assert.Throws<RequestValidationException>(action);
        var error = Assert.Single(exception.ValidationErrors);
        Assert.Equal(
            "ifMatch",
            error.PropertyName);
    }

    [Fact]
    public void ParseOptional_WhenEntityTagIsMissing_ReturnsNull()
    {
        // Arrange

        // Act
        var result = _entityTagService.ParseOptional(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseOptional_WhenEntityTagIsValid_ReturnsVersion()
    {
        // Arrange

        // Act
        var result = _entityTagService.ParseOptional("\"0000002a\"");

        // Assert
        Assert.Equal(
            42u,
            result);
    }

    [Fact]
    public void ParseOptional_WhenEntityTagIsMalformed_ThrowsRequestValidationException()
    {
        // Arrange

        // Act
        var action = () =>
        {
            _ = _entityTagService.ParseOptional("invalid");
        };

        // Assert
        var exception = Assert.Throws<RequestValidationException>(action);
        var error = Assert.Single(exception.ValidationErrors);
        Assert.Equal(
            "ifMatch",
            error.PropertyName);
    }
}
