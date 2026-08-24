using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Handlers;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

/// <summary>
/// Configures the hardened Google OpenID Connect authorization-code flow.
/// </summary>
public static class GoogleAuthenticationExtensions
{
    /// <summary>
    /// Adds Google OpenID Connect and its short-lived external cookie without changing JWT defaults.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddGoogleAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddLogging(logging => logging.AddFilter(
            "Microsoft.AspNetCore.Authentication.OpenIdConnect",
            LogLevel.None));
        var section = configuration.GetSection(GoogleAuthenticationOptions.SectionName);
        var configuredOptions = section.Get<GoogleAuthenticationOptions>() ??
            new GoogleAuthenticationOptions();
        services.AddSingleton<IGoogleReturnPathValidator, GoogleReturnPathValidator>();
        services.AddSingleton<IValidateOptions<GoogleAuthenticationOptions>,
            GoogleAuthenticationOptionsValidator>();
        services.AddOptions<GoogleAuthenticationOptions>()
            .Bind(section)
            .ValidateOnStart();
        services.AddSingleton<IGoogleReturnPathService, GoogleReturnPathService>();
        services.AddSingleton<IGoogleExternalAuthenticationService, GoogleExternalAuthenticationService>();
        var authentication = services.AddAuthentication()
            .AddCookie(
                GoogleAuthenticationSchemes.ExternalCookie,
                options => ConfigureExternalCookie(
                    options,
                    environment));

        if (configuredOptions.Enabled)
        {
            services.AddScoped<GoogleOpenIdConnectEvents>();
            authentication.AddOpenIdConnect(
                GoogleAuthenticationSchemes.OpenIdConnect,
                GoogleAuthenticationConstants.LoginProvider,
                options => ConfigureOpenIdConnect(
                    options,
                    configuredOptions));
            services.PostConfigure<OpenIdConnectOptions>(
                GoogleAuthenticationSchemes.OpenIdConnect,
                ConfigureConfigurationManager);
        }

        return services;
    }

    /// <summary>
    /// Wraps the native configuration cache so provider discovery failures remain classifiable.
    /// </summary>
    /// <param name="options">The OpenID Connect options.</param>
    public static void ConfigureConfigurationManager(OpenIdConnectOptions options)
    {

        if (options.ConfigurationManager is null or GoogleOpenIdConnectConfigurationManager)
            return;

        options.ConfigurationManager = new GoogleOpenIdConnectConfigurationManager(
            options.ConfigurationManager);
    }

    /// <summary>
    /// Configures the short-lived Data Protection external identity cookie.
    /// </summary>
    /// <param name="options">The cookie options.</param>
    /// <param name="environment">The web host environment.</param>
    public static void ConfigureExternalCookie(
        CookieAuthenticationOptions options,
        IWebHostEnvironment environment)
    {
        options.Cookie.Name = environment.IsProduction()
            ? GoogleAuthenticationConstants.ProductionExternalCookieName
            : GoogleAuthenticationConstants.LocalExternalCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.MaxAge = GoogleAuthenticationConstants.TransientLifetime;
        options.ExpireTimeSpan = GoogleAuthenticationConstants.TransientLifetime;
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.CacheControl = "no-store";

            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Configures Google OpenID Connect with code flow, PKCE, nonce and strict token validation.
    /// </summary>
    /// <param name="options">The OpenID Connect options.</param>
    /// <param name="configuration">The validated Google configuration.</param>
    public static void ConfigureOpenIdConnect(
        OpenIdConnectOptions options,
        GoogleAuthenticationOptions configuration)
    {
        options.Authority = GoogleAuthenticationConstants.Authority;
        options.CallbackPath = GoogleAuthenticationConstants.CallbackPath;
        options.ClientId = configuration.ClientId;
        options.ClientSecret = configuration.ClientSecret;
        options.BackchannelTimeout = TimeSpan.FromSeconds(
            configuration.BackchannelTimeoutSeconds);
        options.BackchannelHttpHandler = new GoogleOpenIdConnectBackchannelHandler();
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.FormPost;
        options.UsePkce = true;
        options.SignInScheme = GoogleAuthenticationSchemes.ExternalCookie;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.RemoteAuthenticationTimeout = GoogleAuthenticationConstants.TransientLifetime;
        options.EventsType = typeof(GoogleOpenIdConnectEvents);
        options.Scope.Clear();
        options.Scope.Add(OpenIdConnectScope.OpenId);
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.CorrelationCookie.HttpOnly = true;
        options.CorrelationCookie.IsEssential = true;
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.CorrelationCookie.Expiration = GoogleAuthenticationConstants.TransientLifetime;
        options.NonceCookie.HttpOnly = true;
        options.NonceCookie.IsEssential = true;
        options.NonceCookie.SameSite = SameSiteMode.None;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.Expiration = GoogleAuthenticationConstants.TransientLifetime;
        options.ProtocolValidator.NonceLifetime = GoogleAuthenticationConstants.TransientLifetime;
        options.ProtocolValidator.RequireNonce = true;
        options.ProtocolValidator.RequireTimeStampInNonce = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ClockSkew = GoogleAuthenticationConstants.ClockSkew,
            NameClaimType = "name",
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms =
            [
                SecurityAlgorithms.RsaSha256
            ],
            ValidAudience = configuration.ClientId,
            ValidIssuers =
            [
                GoogleAuthenticationConstants.Authority,
                GoogleAuthenticationConstants.AlternateIssuer
            ]
        };
    }
}
