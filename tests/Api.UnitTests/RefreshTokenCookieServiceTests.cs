using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Http;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class RefreshTokenCookieServiceTests
{
    [Theory]
    [InlineData("Local", RefreshTokenCookieService.LocalCookieName)]
    [InlineData("Production", RefreshTokenCookieService.ProductionCookieName)]
    public void GetCookieName_WhenEnvironmentIsProvided_ReturnsExpectedName(
        string environmentName,
        string expected)
    {
        // Arrange
        var service = CreateService(environmentName);

        // Act
        var result = service.GetCookieName();

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData("Local", false, false)]
    [InlineData("Local", true, true)]
    [InlineData("Production", false, true)]
    public void CreateCookieOptions_WhenEnvironmentIsProvided_UsesExpectedSecurity(
        string environmentName,
        bool requestIsHttps,
        bool expectedSecure)
    {
        // Arrange
        var service = CreateService(environmentName);

        // Act
        var result = service.CreateCookieOptions(
            requestIsHttps,
            null);

        // Assert
        Assert.True(result.HttpOnly);
        Assert.True(result.IsEssential);
        Assert.Equal(
            "/",
            result.Path);
        Assert.Equal(
            SameSiteMode.Strict,
            result.SameSite);
        Assert.Equal(
            expectedSecure,
            result.Secure);
        Assert.Null(result.Domain);
        Assert.Null(result.Expires);
        Assert.Null(result.MaxAge);
    }

    [Fact]
    public void CreateCookieOptions_WhenExpirationIsProvided_UsesAbsoluteExpiration()
    {
        // Arrange
        var service = CreateService("Production");
        var expires = new DateTimeOffset(
            2026,
            9,
            20,
            12,
            0,
            0,
            TimeSpan.Zero);

        // Act
        var result = service.CreateCookieOptions(
            true,
            expires);

        // Assert
        Assert.Equal(
            expires,
            result.Expires);
        Assert.Null(result.MaxAge);
    }

    [Fact]
    public void GetValue_WhenCookieExists_ReturnsRefreshToken()
    {
        // Arrange
        var service = CreateService("Local");
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{RefreshTokenCookieService.LocalCookieName}=refresh-token";

        // Act
        var result = service.GetValue(context.Request);

        // Assert
        Assert.Equal(
            "refresh-token",
            result);
    }

    private static RefreshTokenCookieService CreateService(string environmentName)
    {
        return new RefreshTokenCookieService(new TestWebHostEnvironment(environmentName));
    }
}
