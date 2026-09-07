using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GoogleReturnPathServiceTests
{
    private readonly GoogleReturnPathService _service = new(
        Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions
        {
            Enabled = true,
            FrontendOrigin = "https://app.example.test",
            DefaultReturnPath = "/login/google-return",
            AllowedReturnPaths =
            ["/login/google-return"]
        }),
        new GoogleReturnPathValidator());

    [Fact]
    public void Resolve_WhenPathIsMissing_ReturnsConfiguredDefault()
    {
        // Arrange

        // Act
        var result = _service.Resolve(null);

        // Assert
        Assert.Equal(
            "/login/google-return",
            result);
    }

    [Fact]
    public void Resolve_WhenPathAndConfiguredDefaultAreMissing_ThrowsRequestValidationException()
    {
        // Arrange
        var service = new GoogleReturnPathService(
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions
            {
                Enabled = true,
                FrontendOrigin = "https://app.example.test",
                AllowedReturnPaths =
                ["/login/google-return"]
            }),
            new GoogleReturnPathValidator());

        // Act
        string action() => service.Resolve(null);

        // Assert
        Assert.Throws<RequestValidationException>(
            (Func<string>)action);
    }

    [Fact]
    public void Resolve_WhenAllowlistIsMissing_ThrowsRequestValidationException()
    {
        // Arrange
        var service = new GoogleReturnPathService(
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions
            {
                Enabled = true,
                FrontendOrigin = "https://app.example.test",
                DefaultReturnPath = "/login/google-return"
            }),
            new GoogleReturnPathValidator());

        // Act
        string action() => service.Resolve("/login/google-return");

        // Assert
        Assert.Throws<RequestValidationException>(
            (Func<string>)action);
    }

    [Fact]
    public void Resolve_WhenPathIsAllowlisted_ReturnsExactPath()
    {
        // Arrange

        // Act
        var result = _service.Resolve("/login/google-return");

        // Assert
        Assert.Equal(
            "/login/google-return",
            result);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("/unknown")]
    [InlineData("/login/google-return?next=/")]
    public void Resolve_WhenPathIsNotAllowlisted_ThrowsRequestValidationException(string returnPath)
    {
        // Arrange

        // Act
        string action() => _service.Resolve(returnPath);

        // Assert
        var exception = Assert.Throws<RequestValidationException>(
            (Func<string>)action);
        var error = Assert.Single(exception.ValidationErrors);
        Assert.Equal(
            "returnPath",
            error.PropertyName);
    }

    [Fact]
    public void BuildAbsoluteUri_WhenPathIsFixed_ReturnsConfiguredFrontendUri()
    {
        // Arrange

        // Act
        var result = _service.BuildAbsoluteUri("/login/google-return#error=failed");

        // Assert
        Assert.Equal(
            "https://app.example.test/login/google-return#error=failed",
            result);
    }
}
