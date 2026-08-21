using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Net.Http.Headers;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents identity authentication extensions.
/// </summary>

public static class IdentityAuthenticationExtensions
{
    private const string LocalCookieName = "MonKado.Auth";
    private const string ProductionCookieName = "__Host-MonKado.Auth";
    /// <summary>
    /// Executes the add identity authentication operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        services.AddAuthorization();
        services
            .AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((
                options,
                ticketStore) =>
            {
                options.Cookie.Name = environment.IsProduction()
                    ? ProductionCookieName
                    : LocalCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsProduction()
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SessionStore = ticketStore;
                options.SlidingExpiration = true;
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
            });

        return services;
    }
    /// <summary>
    /// Executes the use identity authentication operation.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The operation result.</returns>

    public static IApplicationBuilder UseIdentityAuthentication(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.UseAuthentication();
        application.UseAuthorization();

        return application;
    }
}
