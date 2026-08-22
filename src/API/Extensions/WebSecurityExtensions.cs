using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents web security extensions.
/// </summary>

public static class WebSecurityExtensions
{
    /// <summary>
    /// Identifies frontend cors policy.
    /// </summary>
    public const string FrontendCorsPolicy = "Frontend";

    private const string LocalAntiforgeryCookieName = "MonKado.Antiforgery";
    private const string ProductionAntiforgeryCookieName = "__Host-MonKado.Antiforgery";

    private static readonly string[] _allowedMethods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
        HttpMethods.Options
    ];
    /// <summary>
    /// Executes the add web security operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection AddWebSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(WebSecurityOptions.SectionName);
        var options = section.Get<WebSecurityOptions>() ?? new WebSecurityOptions();
        ValidateConfiguration(
            options,
            configuration["AllowedHosts"],
            environment);

        services.Configure<WebSecurityOptions>(section);
        services.AddCors(cors => ConfigureCors(
            cors,
            options));

        services.Configure<CookiePolicyOptions>(cookiePolicy =>
        {
            cookiePolicy.HttpOnly = HttpOnlyPolicy.Always;
            cookiePolicy.Secure = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            cookiePolicy.MinimumSameSitePolicy = Microsoft.AspNetCore.Http.SameSiteMode.Unspecified;
        });

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = WebSecurityOptions.AntiforgeryHeaderName;
            antiforgery.Cookie.Name = environment.IsProduction()
                ? ProductionAntiforgeryCookieName
                : LocalAntiforgeryCookieName;
            antiforgery.Cookie.HttpOnly = true;
            antiforgery.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            antiforgery.Cookie.SecurePolicy = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            antiforgery.Cookie.Path = "/";
            antiforgery.Cookie.IsEssential = true;
        });

        return services;
    }
    /// <summary>
    /// Executes the use web security operation.
    /// </summary>
    /// <param name="app">The app.</param>
    /// <returns>The operation result.</returns>

    public static WebApplication UseWebSecurity(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (
            context,
            next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers[HeaderNames.ContentSecurityPolicy] =
                    "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
                headers[HeaderNames.XFrameOptions] = "DENY";
                headers[HeaderNames.XContentTypeOptions] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                headers["Cross-Origin-Resource-Policy"] = "same-site";

                if (ShouldAddStrictTransportSecurity(
                    app.Environment.IsProduction(),
                    context.Request.IsHttps))
                    headers[HeaderNames.StrictTransportSecurity] = "max-age=31536000";

                return Task.CompletedTask;
            });

            await next(context);
        });

        app.UseCookiePolicy();
        app.UseCors(FrontendCorsPolicy);

        return app;
    }
    /// <summary>
    /// Executes the map web security operation.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <returns>The operation result.</returns>

    public static IEndpointRouteBuilder MapWebSecurity(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            "/security/csrf-token",
            (
                HttpContext context,
                IAntiforgery antiforgery) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(context);
                    context.Response.Headers.CacheControl = "no-store";
                    ArgumentNullException.ThrowIfNull(tokens.RequestToken);

                    return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken));
                })
            .WithName("GetCsrfToken");

        return endpoints;
    }

    internal static bool ShouldAddStrictTransportSecurity(
        bool isProduction,
        bool isHttps)
    {

        return isProduction && isHttps;
    }

    internal static void ConfigureCors(
        CorsOptions cors,
        WebSecurityOptions options)
    {
        cors.AddPolicy(
            FrontendCorsPolicy,
            policy => policy
                .WithOrigins(options.AllowedOrigins)
                .WithMethods(_allowedMethods)
                .WithHeaders(
                    HeaderNames.ContentType,
                    HeaderNames.Authorization,
                    WebSecurityOptions.AntiforgeryHeaderName)
                .AllowCredentials()
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
    }

    internal static void ValidateConfiguration(
        WebSecurityOptions options,
        string? allowedHosts,
        IWebHostEnvironment environment)
    {

        if (options.AllowedOrigins.Length == 0)
        {

            throw new InvalidOperationException(
                "At least one exact frontend origin is required in 'WebSecurity:AllowedOrigins'.");
        }

        foreach (var origin in options.AllowedOrigins)
        {
            ValidateOrigin(
                origin,
                environment);
        }

        ValidateAllowedHosts(allowedHosts);

    }

    internal static void ValidateOrigin(
        string origin,
        IWebHostEnvironment environment)
    {

        if (string.IsNullOrWhiteSpace(origin) || origin.Contains(
            '*',
            StringComparison.Ordinal))
            throw new InvalidOperationException("Frontend origins must be explicit and cannot contain wildcards.");

        if (!Uri.TryCreate(
            origin,
            UriKind.Absolute,
            out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            origin.EndsWith('/'))
        {

            throw new InvalidOperationException(
                $"Frontend origin '{origin}' must contain only an HTTP or HTTPS scheme, host, and optional port.");
        }

        if (environment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Frontend origins must use HTTPS in Production.");

        if (!environment.IsProduction() && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException("Plain HTTP frontend origins are allowed only for localhost.");
    }

    internal static void ValidateAllowedHosts(string? allowedHosts)
    {
        var hosts = allowedHosts?
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (hosts.Length == 0)
            throw new InvalidOperationException("At least one explicit API host is required in 'AllowedHosts'.");

        foreach (var host in hosts)
        {

            if (host.Contains(
                '*',
                StringComparison.Ordinal) ||
                host.Equals(
                    "0.0.0.0",
                    StringComparison.Ordinal) ||
                host.Equals(
                    "[::]",
                    StringComparison.Ordinal) ||
                host.Contains(
                    "://",
                    StringComparison.Ordinal) ||
                host.Contains(
                    '/',
                    StringComparison.Ordinal) ||
                Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {

                throw new InvalidOperationException(
                    $"Allowed API host '{host}' must be an explicit host name without scheme, path, port, or wildcard.");
            }
        }
    }
}
