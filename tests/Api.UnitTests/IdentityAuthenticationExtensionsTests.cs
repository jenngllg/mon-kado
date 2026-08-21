using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class IdentityAuthenticationExtensionsTests
{
    [Theory]
    [InlineData("Local", "MonKado.Auth", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", "__Host-MonKado.Auth", CookieSecurePolicy.Always)]
    public async Task AddIdentityAuthentication_WhenOptionsAreResolved_ConfiguresCookieAndRedirects(
        string environmentName,
        string expectedCookieName,
        CookieSecurePolicy expectedSecurePolicy)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITicketStore, TestTicketStore>();
        services.AddIdentityAuthentication(new TestWebHostEnvironment(environmentName));
        await using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        // Act
        var options = monitor.Get(IdentityConstants.ApplicationScheme);
        var loginContext = CreateRedirectContext(
            provider,
            options);
        await options.Events.OnRedirectToLogin(loginContext);
        var deniedContext = CreateRedirectContext(
            provider,
            options);
        await options.Events.OnRedirectToAccessDenied(deniedContext);

        // Assert
        Assert.Equal(
            expectedCookieName,
            options.Cookie.Name);
        Assert.Equal(
            expectedSecurePolicy,
            options.Cookie.SecurePolicy);
        Assert.Same(
            provider.GetRequiredService<ITicketStore>(),
            options.SessionStore);
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            loginContext.Response.StatusCode);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            deniedContext.Response.StatusCode);
        Assert.Equal(
            "no-store",
            loginContext.Response.Headers.CacheControl);
        Assert.Equal(
            "no-store",
            deniedContext.Response.Headers.CacheControl);
    }

    private static RedirectContext<CookieAuthenticationOptions> CreateRedirectContext(
        IServiceProvider provider,
        CookieAuthenticationOptions options)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));

        return new RedirectContext<CookieAuthenticationOptions>(
            context,
            scheme,
            options,
            new AuthenticationProperties(),
            "/login");
    }
}
