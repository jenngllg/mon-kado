using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;

using MediatR;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

public class GoogleAuthenticationExtensionsTests
{
    [Theory]
    [InlineData("Local", GoogleAuthenticationConstants.LocalExternalCookieName)]
    [InlineData("Production", GoogleAuthenticationConstants.ProductionExternalCookieName)]
    public async Task ConfigureExternalCookie_WhenEnvironmentIsKnown_UsesHardenedShortLivedCookie(
        string environmentName,
        string expectedName)
    {
        // Arrange
        var options = new CookieAuthenticationOptions();
        var environment = new TestWebHostEnvironment(environmentName);
        var scheme = new AuthenticationScheme(
            GoogleAuthenticationSchemes.ExternalCookie,
            GoogleAuthenticationSchemes.ExternalCookie,
            typeof(CookieAuthenticationHandler));

        // Act
        GoogleAuthenticationExtensions.ConfigureExternalCookie(
            options,
            environment);
        var loginContext = new RedirectContext<CookieAuthenticationOptions>(
            new DefaultHttpContext(),
            scheme,
            options,
            new AuthenticationProperties(),
            "https://api.example.test/login");
        await options.Events.RedirectToLogin(loginContext);
        var accessDeniedContext = new RedirectContext<CookieAuthenticationOptions>(
            new DefaultHttpContext(),
            scheme,
            options,
            new AuthenticationProperties(),
            "https://api.example.test/denied");
        await options.Events.RedirectToAccessDenied(accessDeniedContext);

        // Assert
        Assert.Equal(
            expectedName,
            options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.True(options.Cookie.IsEssential);
        Assert.Equal(
            "/",
            options.Cookie.Path);
        Assert.Equal(
            SameSiteMode.Lax,
            options.Cookie.SameSite);
        Assert.Equal(
            CookieSecurePolicy.Always,
            options.Cookie.SecurePolicy);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            options.Cookie.MaxAge);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            loginContext.Response.StatusCode);
        Assert.Equal(
            "no-store",
            loginContext.Response.Headers.CacheControl);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            accessDeniedContext.Response.StatusCode);
        Assert.Equal(
            "no-store",
            accessDeniedContext.Response.Headers.CacheControl);
    }

    [Fact]
    public void ConfigureOpenIdConnect_WhenConfigurationIsValid_UsesCodePkceAndMinimalScopes()
    {
        // Arrange
        var options = new OpenIdConnectOptions();
        var configuration = CreateGoogleOptions();

        // Act
        GoogleAuthenticationExtensions.ConfigureOpenIdConnect(
            options,
            configuration);

        // Assert
        Assert.Equal(
            GoogleAuthenticationConstants.Authority,
            options.Authority);
        Assert.Equal(
            GoogleAuthenticationConstants.CallbackPath,
            options.CallbackPath);
        Assert.Equal(
            configuration.ClientId,
            options.ClientId);
        Assert.Equal(
            configuration.ClientSecret,
            options.ClientSecret);
        Assert.Equal(
            OpenIdConnectResponseType.Code,
            options.ResponseType);
        Assert.Equal(
            OpenIdConnectResponseMode.FormPost,
            options.ResponseMode);
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.False(options.MapInboundClaims);
        Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.UseSecurityTokenValidator);
        Assert.Equal(
            TimeSpan.FromSeconds(15),
            options.BackchannelTimeout);
        Assert.IsType<GoogleOpenIdConnectBackchannelHandler>(options.BackchannelHttpHandler);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            options.RemoteAuthenticationTimeout);
        Assert.Equal(
            GoogleAuthenticationSchemes.ExternalCookie,
            options.SignInScheme);
        Assert.Equal(
            [
                "openid",
                "email",
                "profile"
            ],
            options.Scope);
        Assert.DoesNotContain(
            "offline_access",
            options.Scope);
        Assert.Equal(
            SameSiteMode.None,
            options.CorrelationCookie.SameSite);
        Assert.True(options.CorrelationCookie.HttpOnly);
        Assert.True(options.CorrelationCookie.IsEssential);
        Assert.Equal(
            CookieSecurePolicy.Always,
            options.CorrelationCookie.SecurePolicy);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            options.CorrelationCookie.Expiration);
        Assert.Equal(
            SameSiteMode.None,
            options.NonceCookie.SameSite);
        Assert.True(options.NonceCookie.HttpOnly);
        Assert.True(options.NonceCookie.IsEssential);
        Assert.Equal(
            CookieSecurePolicy.Always,
            options.NonceCookie.SecurePolicy);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            options.NonceCookie.Expiration);
        Assert.Equal(
            GoogleAuthenticationConstants.TransientLifetime,
            options.ProtocolValidator.NonceLifetime);
        Assert.True(options.ProtocolValidator.RequireNonce);
        Assert.True(options.ProtocolValidator.RequireTimeStampInNonce);
        Assert.Equal(
            GoogleAuthenticationConstants.ClockSkew,
            options.TokenValidationParameters.ClockSkew);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.Equal(
            configuration.ClientId,
            options.TokenValidationParameters.ValidAudience);
        Assert.Equal(
            [SecurityAlgorithms.RsaSha256],
            options.TokenValidationParameters.ValidAlgorithms);
        Assert.Equal(
            [
                GoogleAuthenticationConstants.Authority,
                GoogleAuthenticationConstants.AlternateIssuer
            ],
            options.TokenValidationParameters.ValidIssuers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureConfigurationManager_WhenManagerNeedsNoWrapping_PreservesIt(
        bool hasWrappedManager)
    {
        // Arrange
        var innerManagerMock = new Mock<IConfigurationManager<OpenIdConnectConfiguration>>(
            MockBehavior.Strict);
        var manager = hasWrappedManager
            ? new GoogleOpenIdConnectConfigurationManager(innerManagerMock.Object)
            : null;
        var options = new OpenIdConnectOptions
        {
            ConfigurationManager = manager
        };

        // Act
        GoogleAuthenticationExtensions.ConfigureConfigurationManager(options);

        // Assert
        Assert.Same(
            manager,
            options.ConfigurationManager);
        innerManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddGoogleAuthentication_WhenEnabled_PreservesJwtDefaultAndAddsGoogleSchemes()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(enabled: true);
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection()
            .UseEphemeralDataProtectionProvider();
        var senderMock = new Mock<ISender>(MockBehavior.Strict);
        services.AddSingleton(senderMock.Object);
        services.AddJwtAuthentication(configuration);

        // Act
        services.AddGoogleAuthentication(
            configuration,
            new TestWebHostEnvironment("Local"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaults = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthenticationOptions>>()
            .Value;
        var googleScheme = await schemes.GetSchemeAsync(GoogleAuthenticationSchemes.OpenIdConnect);
        var externalScheme = await schemes.GetSchemeAsync(GoogleAuthenticationSchemes.ExternalCookie);
        var logging = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        var openIdConnectOptions = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(GoogleAuthenticationSchemes.OpenIdConnect);

        // Assert
        Assert.Equal(
            JwtBearerDefaults.AuthenticationScheme,
            defaults.DefaultScheme);
        Assert.NotNull(googleScheme);
        Assert.NotNull(externalScheme);
        Assert.IsType<GoogleOpenIdConnectConfigurationManager>(
            openIdConnectOptions.ConfigurationManager);
        Assert.Contains(
            logging.Rules,
            rule => rule.CategoryName ==
                    "Microsoft.AspNetCore.Authentication.OpenIdConnect" &&
                rule.LogLevel == LogLevel.None);
        senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddGoogleAuthentication_WhenDisabled_DoesNotRegisterRemoteScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfigurationWithoutGoogleSection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection()
            .UseEphemeralDataProtectionProvider();
        services.AddJwtAuthentication(configuration);

        // Act
        services.AddGoogleAuthentication(
            configuration,
            new TestWebHostEnvironment("Local"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var googleScheme = await schemes.GetSchemeAsync(GoogleAuthenticationSchemes.OpenIdConnect);
        var externalScheme = await schemes.GetSchemeAsync(GoogleAuthenticationSchemes.ExternalCookie);

        // Assert
        Assert.Null(googleScheme);
        Assert.NotNull(externalScheme);
    }

    private static GoogleAuthenticationOptions CreateGoogleOptions()
    {

        return new GoogleAuthenticationOptions
        {
            Enabled = true,
            ClientId = "client.apps.googleusercontent.com",
            ClientSecret = "client-secret",
            FrontendOrigin = "https://app.example.test",
            DefaultReturnPath = "/my-lists",
            AllowedReturnPaths =
            [
                "/my-lists"
            ]
        };
    }

    private static IConfiguration CreateConfiguration(bool enabled)
    {

        return new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new("Jwt:Audience", "MonKado.Frontend"),
                new("Jwt:Issuer", "MonKado.Api"),
                new("Jwt:SigningKey", "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="),
                new("GoogleAuthentication:Enabled", enabled.ToString()),
                new("GoogleAuthentication:ClientId", "client.apps.googleusercontent.com"),
                new("GoogleAuthentication:ClientSecret", "client-secret"),
                new("GoogleAuthentication:FrontendOrigin", "https://app.example.test"),
                new("GoogleAuthentication:DefaultReturnPath", "/my-lists"),
                new("GoogleAuthentication:AllowedReturnPaths:0", "/my-lists")
            ])
            .Build();
    }

    private static IConfiguration CreateConfigurationWithoutGoogleSection()
    {

        return new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new("Jwt:Audience", "MonKado.Frontend"),
                new("Jwt:Issuer", "MonKado.Api"),
                new("Jwt:SigningKey", "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=")
            ])
            .Build();
    }
}
