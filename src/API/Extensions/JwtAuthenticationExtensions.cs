using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

/// <summary>
/// Configures JWT authentication.
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Adds JWT authentication services.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddOptions<JwtOptions>()
            .Bind(section)
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((
                options,
                jwtOptions) => ConfigureBearerOptions(
                    options,
                    jwtOptions.Value));
        services.AddAuthorization(options => options.AddPolicy(
            AuthorizationPolicies.CurrentSession,
            policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            }));

        return services;
    }

    /// <summary>
    /// Adds JWT authentication and authorization middleware.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static WebApplication UseJwtAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    internal static void ConfigureBearerOptions(
        JwtBearerOptions options,
        JwtOptions jwtOptions)
    {
        options.IncludeErrorDetails = false;
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ClockSkew = TimeSpan.FromSeconds(30),
            IssuerSigningKey = new SymmetricSecurityKey(
                Convert.FromBase64String(jwtOptions.SigningKey)),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms =
            [
                SecurityAlgorithms.HmacSha256
            ],
            ValidAudience = jwtOptions.Audience,
            ValidIssuer = jwtOptions.Issuer
        };
    }
}
