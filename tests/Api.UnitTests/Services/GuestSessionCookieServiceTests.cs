using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Http;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GuestSessionCookieServiceTests
{
    private static readonly DateTime _expiresAt = new(
        2027,
        2,
        22,
        12,
        0,
        0,
        DateTimeKind.Utc);

    [Theory]
    [InlineData("Local", false, "MonKado.Guest", false)]
    [InlineData("Local", true, "MonKado.Guest", true)]
    [InlineData("Production", false, "__Host-MonKado.Guest", true)]
    public void Append_WhenEnvironmentVaries_UsesExpectedSecurity(
        string environmentName,
        bool requestIsHttps,
        string expectedName,
        bool expectedSecure)
    {
        // Arrange
        var service = CreateService(environmentName);
        var context = new DefaultHttpContext();
        context.Request.Scheme = requestIsHttps
            ? "https"
            : "http";

        // Act
        service.Append(
            context,
            "guest-token",
            _expiresAt);

        // Assert
        var header = Assert.IsType<string>(Assert.Single(context.Response.Headers.SetCookie));
        Assert.StartsWith(
            $"{expectedName}=guest-token;",
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
        Assert.Contains(
            "expires=",
            header,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            expectedSecure,
            header.Contains(
                "secure",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetValue_WhenCookieExists_ReturnsGuestToken()
    {
        // Arrange
        var service = CreateService("Local");
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "MonKado.Guest=guest-token";

        // Act
        var result = service.GetValue(context.Request);

        // Assert
        Assert.Equal(
            "guest-token",
            result);
    }

    [Fact]
    public void Delete_WhenCalled_ExpiresEnvironmentCookie()
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
            "__Host-MonKado.Guest=;",
            header,
            StringComparison.Ordinal);
        Assert.Contains(
            "expires=",
            header,
            StringComparison.OrdinalIgnoreCase);
    }

    private static GuestSessionCookieService CreateService(string environmentName)
    {
        return new GuestSessionCookieService(new TestWebHostEnvironment(environmentName));
    }
}
