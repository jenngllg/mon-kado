using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.Configurations;
/// <summary>
/// Represents api injection configuration.
/// </summary>

public static class ApiInjectionConfiguration
{
    /// <summary>
    /// Executes the configure api injection operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>
    public static IServiceCollection ConfigureApiInjection(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddJwtAuthentication(configuration);
        services.AddGoogleAuthentication(
            configuration,
            environment);
        services.AddSingleton<IRefreshTokenCookieService, RefreshTokenCookieService>();
        services.AddSingleton<IGuestSessionCookieService, GuestSessionCookieService>();
        services.AddSingleton<IEntityTagService, EntityTagService>();
        services.AddSingleton<IWishlistShareLinkUrlService, WishlistShareLinkUrlService>();
        services.AddSingleton<IValidateOptions<WishlistSharingOptions>, WishlistSharingOptionsValidator>();
        services.AddOptions<WishlistSharingOptions>()
            .Bind(configuration.GetSection(WishlistSharingOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, WishlistOwnerAuthorizationHandler>();
        services.ConfigureDataProtection(
            configuration,
            environment);
        services
            .AddControllersWithViews()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(
                    JsonNamingPolicy.CamelCase,
                    allowIntegerValues: false)));
        services.AddApiHealthChecks();
        services.AddApiOpenApi();
        services.AddTrustedReverseProxy(
            configuration,
            environment);
        services.AddWebSecurity(
            configuration,
            environment);
        services.AddApiErrorResponses();
        services.AddAuthenticationRateLimiting();

        return services;
    }
}
