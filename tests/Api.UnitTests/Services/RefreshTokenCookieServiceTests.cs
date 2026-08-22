using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Http;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class RefreshTokenCookieServiceTests
{
    [Theory]
    [InlineData("Local", false, "MonKado.Refresh", false)]
    [InlineData("Local", true, "MonKado.Refresh", true)]
    [InlineData("Production", false, "__Host-MonKado.Refresh", true)]
    public void Append_WhenEnvironmentIsProvided_UsesExpectedSecurity(
        string environmentName,
        bool requestIsHttps,
        string expectedName,
        bool expectedSecure)
    {
        // Arrange
        var service = CreateService(environmentName);
        var context = new DefaultHttpContext();
        context.Request.Scheme = requestIsHttps ? "https" : "http";
        var tokens = CreateTokens(isPersistent: false);

        // Act
        service.Append(
            context,
            tokens);

        // Assert
        var header = Assert.IsType<string>(Assert.Single(context.Response.Headers.SetCookie));
        Assert.StartsWith(
            $"{expectedName}=refresh-token;",
            header,
            StringComparison.Ordinal);
        Assert.Contains(
            "path=/",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "httponly",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "samesite=strict",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            expectedSecure,
            header.Contains(
                "secure",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            "expires=",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "max-age=",
            header,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Append_WhenSessionIsPersistent_UsesAbsoluteExpiration()
    {
        // Arrange
        var service = CreateService("Production");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        var tokens = CreateTokens(isPersistent: true);

        // Act
        service.Append(
            context,
            tokens);

        // Assert
        var header = Assert.IsType<string>(Assert.Single(context.Response.Headers.SetCookie));
        Assert.Contains(
            "expires=",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "max-age=",
            header,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delete_WhenCalled_ExpiresTheEnvironmentCookie()
    {
        // Arrange
        var service = CreateService("Production");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        // Act
        service.Delete(context);

        // Assert
        var header = Assert.IsType<string>(Assert.Single(context.Response.Headers.SetCookie));
        Assert.StartsWith(
            "__Host-MonKado.Refresh=;",
            header,
            StringComparison.Ordinal);
        Assert.Contains(
            "expires=",
            header,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetValue_WhenCookieExists_ReturnsRefreshToken()
    {
        // Arrange
        var service = CreateService("Local");
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie =
            "MonKado.Refresh=refresh-token";

        // Act
        var result = service.GetValue(context.Request);

        // Assert
        Assert.Equal(
            "refresh-token",
            result);
    }

    private static AccountSessionTokens CreateTokens(bool isPersistent)
    {
        return new AccountSessionTokens(
            new AccessToken(
                "access-token",
                900),
            "refresh-token",
            new DateTime(
                2026,
                9,
                20,
                12,
                0,
                0,
                DateTimeKind.Utc),
            isPersistent);
    }

    private static RefreshTokenCookieService CreateService(string environmentName)
    {
        return new RefreshTokenCookieService(new TestWebHostEnvironment(environmentName));
    }
}
