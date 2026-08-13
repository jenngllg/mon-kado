using JennGllg.Fr.MonKado.Back.Api.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class WebSecurityExtensions
{
    public const string FrontendCorsPolicy = "Frontend";

    private const string LocalAntiforgeryCookieName = "MonKado.Antiforgery";
    private const string ProductionAntiforgeryCookieName = "__Host-MonKado.Antiforgery";

    private static readonly string[] AllowedMethods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
        HttpMethods.Options
    ];

    public static IServiceCollection AddWebSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        IConfigurationSection section = configuration.GetSection(WebSecurityOptions.SectionName);
        WebSecurityOptions options = section.Get<WebSecurityOptions>() ?? new WebSecurityOptions();
        ValidateConfiguration(options, configuration["AllowedHosts"], environment);

        services.Configure<WebSecurityOptions>(section);
        services.AddCors(cors => cors.AddPolicy(
            FrontendCorsPolicy,
            policy => policy
                .WithOrigins(options.AllowedOrigins)
                .WithMethods(AllowedMethods)
                .WithHeaders(HeaderNames.ContentType, WebSecurityOptions.AntiforgeryHeaderName)
                .AllowCredentials()
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

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

        services.Configure<MvcOptions>(mvc =>
            mvc.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

        return services;
    }

    public static WebApplication UseWebSecurity(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                IHeaderDictionary headers = context.Response.Headers;
                headers[HeaderNames.ContentSecurityPolicy] =
                    "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
                headers[HeaderNames.XFrameOptions] = "DENY";
                headers[HeaderNames.XContentTypeOptions] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                headers["Cross-Origin-Resource-Policy"] = "same-site";

                if (app.Environment.IsProduction() && context.Request.IsHttps)
                {
                    headers[HeaderNames.StrictTransportSecurity] = "max-age=31536000";
                }

                return Task.CompletedTask;
            });

            await next(context);
        });

        app.UseCookiePolicy();
        app.UseCors(FrontendCorsPolicy);

        return app;
    }

    public static IEndpointRouteBuilder MapWebSecurity(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/security/csrf-token",
                (HttpContext context, IAntiforgery antiforgery) =>
                {
                    AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
                    context.Response.Headers.CacheControl = "no-store";

                    return TypedResults.Ok(new CsrfTokenResponse(
                        tokens.RequestToken ?? throw new InvalidOperationException(
                            "ASP.NET Core did not generate an antiforgery request token.")));
                })
            .WithName("GetCsrfToken");

        return endpoints;
    }

    private static void ValidateConfiguration(
        WebSecurityOptions options,
        string? allowedHosts,
        IWebHostEnvironment environment)
    {
        if (options.AllowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "At least one exact frontend origin is required in 'WebSecurity:AllowedOrigins'.");
        }

        foreach (string origin in options.AllowedOrigins)
        {
            ValidateOrigin(origin, environment);
        }

        ValidateAllowedHosts(allowedHosts);

    }

    private static void ValidateOrigin(string origin, IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Contains('*', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Frontend origins must be explicit and cannot contain wildcards.");
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
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
        {
            throw new InvalidOperationException("Frontend origins must use HTTPS in Production.");
        }

        if (!environment.IsProduction() && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new InvalidOperationException("Plain HTTP frontend origins are allowed only for localhost.");
        }
    }

    private static void ValidateAllowedHosts(string? allowedHosts)
    {
        string[] hosts = allowedHosts?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (hosts.Length == 0)
        {
            throw new InvalidOperationException("At least one explicit API host is required in 'AllowedHosts'.");
        }

        foreach (string host in hosts)
        {
            if (host.Contains('*', StringComparison.Ordinal) ||
                host.Equals("0.0.0.0", StringComparison.Ordinal) ||
                host.Equals("[::]", StringComparison.Ordinal) ||
                host.Contains("://", StringComparison.Ordinal) ||
                host.Contains('/', StringComparison.Ordinal) ||
                Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                throw new InvalidOperationException(
                    $"Allowed API host '{host}' must be an explicit host name without scheme, path, port, or wildcard.");
            }
        }
    }
}
