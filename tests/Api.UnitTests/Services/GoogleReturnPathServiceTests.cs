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
            DefaultReturnPath = "/my-lists",
            AllowedReturnPaths =
            [
                "/my-lists"
            ]
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
            "/my-lists",
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
                [
                    "/my-lists"
                ]
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
                DefaultReturnPath = "/my-lists"
            }),
            new GoogleReturnPathValidator());

        // Act
        string action() => service.Resolve("/my-lists");

        // Assert
        Assert.Throws<RequestValidationException>(
            (Func<string>)action);
    }

    [Fact]
    public void Resolve_WhenPathIsAllowlisted_ReturnsExactPath()
    {
        // Arrange

        // Act
        var result = _service.Resolve("/my-lists");

        // Assert
        Assert.Equal(
            "/my-lists",
            result);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("/unknown")]
    [InlineData("/my-lists?next=/")]
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
        var result = _service.BuildAbsoluteUri("/#/login?error=google_auth_failed");

        // Assert
        Assert.Equal(
            "https://app.example.test/#/login?error=google_auth_failed",
            result);
    }
}
