using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class GoogleAuthenticationApiFactory : WebApplicationFactory<Program>
{
    public const string ClientId = "functional.apps.googleusercontent.com";
    public const string Issuer = "https://accounts.google.com";

    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=functional-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    public GoogleAuthenticationApiFactory(
        bool isEnabled = true,
        string? dataProtectionKeysPath = null,
        bool isDiscoveryUnavailable = false)
    {
        _isEnabled = isEnabled;
        _dataProtectionKeysPath = dataProtectionKeysPath;
        _isDiscoveryUnavailable = isDiscoveryUnavailable;
        TimeProvider = new FixedGoogleTimeProvider(new DateTimeOffset(
            2030,
            1,
            1,
            10,
            0,
            0,
            TimeSpan.Zero));
        Backchannel = new FakeGoogleOpenIdConnectBackchannel(TimeProvider);
        GoogleSessionService = new RecordingGoogleAccountSessionService(TimeProvider);
        RefreshSessionService = new RecordingRefreshSessionService(TimeProvider);
    }

    public FixedGoogleTimeProvider TimeProvider
    {
        get;
    }

    public FakeGoogleOpenIdConnectBackchannel Backchannel
    {
        get;
    }

    public RecordingGoogleAccountSessionService GoogleSessionService
    {
        get;
    }

    public RecordingRefreshSessionService RefreshSessionService
    {
        get;
    }

    public IReadOnlyCollection<string> LogMessages => _logProvider.Messages;

    public HttpClient CreateGoogleClient(bool handleCookies = true)
    {

        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Local");
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            UnavailableConnectionString);
        builder.UseSetting(
            "AllowedHosts",
            "localhost");
        builder.UseSetting(
            "WebSecurity:AllowedOrigins:0",
            "https://app.example.test");
        builder.UseSetting(
            "Jwt:SigningKey",
            "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=");
        builder.UseSetting(
            "ReverseProxy:KnownNetworks:0",
            "127.0.0.0/8");
        builder.UseSetting(
            "GoogleAuthentication:Enabled",
            _isEnabled.ToString());
        builder.UseSetting(
            "GoogleAuthentication:ClientId",
            ClientId);
        builder.UseSetting(
            "GoogleAuthentication:ClientSecret",
            "functional-client-secret");
        builder.UseSetting(
            "GoogleAuthentication:FrontendOrigin",
            "https://app.example.test");
        builder.UseSetting(
            "WishlistSharing:FrontendOrigin",
            "https://app.example.test");
        builder.UseSetting(
            "GoogleAuthentication:DefaultReturnPath",
            "/my-lists");
        builder.UseSetting(
            "GoogleAuthentication:AllowedReturnPaths:0",
            "/my-lists");

        if (_dataProtectionKeysPath is not null)
            builder.UseSetting(
                "DataProtection:KeysPath",
                _dataProtectionKeysPath);

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(_logProvider);
        });
        builder.ConfigureServices(services =>
        {

            if (_dataProtectionKeysPath is null)
                services
                    .AddDataProtection()
                    .UseEphemeralDataProtectionProvider();

            services.RemoveAll<IGoogleAccountSessionService>();
            services.AddSingleton<IGoogleAccountSessionService>(GoogleSessionService);
            services.RemoveAll<IRefreshSessionService>();
            services.AddSingleton<IRefreshSessionService>(RefreshSessionService);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);
            services.PostConfigure<OpenIdConnectOptions>(
                GoogleAuthenticationSchemes.OpenIdConnect,
                options => ConfigureProvider(options));
            services.PostConfigure<CookieAuthenticationOptions>(
                GoogleAuthenticationSchemes.ExternalCookie,
                options => options.TimeProvider = TimeProvider);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            Backchannel.Dispose();
    }

    private void ConfigureProvider(OpenIdConnectOptions options)
    {

        if (_isDiscoveryUnavailable)
        {
            options.Configuration = null;
            options.ConfigurationManager = new GoogleOpenIdConnectConfigurationManager(
                new UnavailableOpenIdConnectConfigurationManager());

            return;
        }

        var configuration = new OpenIdConnectConfiguration
        {
            AuthorizationEndpoint = "https://provider.example.test/authorize",
            Issuer = Issuer,
            TokenEndpoint = "https://provider.example.test/token"
        };
        configuration.SigningKeys.Add(Backchannel.SigningKey);
        options.Configuration = configuration;
        options.ConfigurationManager =
            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        options.BackchannelHttpHandler = null;
        options.Backchannel = new HttpClient(
            new GoogleOpenIdConnectBackchannelHandler(Backchannel),
            disposeHandler: false);
        options.TimeProvider = TimeProvider;
        options.TokenValidationParameters.LifetimeValidator = (
            notBefore,
            expires,
            token,
            parameters) =>
        {
            _ = token;
            var now = TimeProvider.GetUtcNow().UtcDateTime;
            var clockSkew = parameters.ClockSkew;

            return notBefore.HasValue &&
                expires.HasValue &&
                notBefore.Value <= now.Add(clockSkew) &&
                expires.Value >= now.Subtract(clockSkew);
        };
    }

    private readonly CapturingLoggerProvider _logProvider = new();
    private readonly string? _dataProtectionKeysPath;
    private readonly bool _isDiscoveryUnavailable;
    private readonly bool _isEnabled;
}
