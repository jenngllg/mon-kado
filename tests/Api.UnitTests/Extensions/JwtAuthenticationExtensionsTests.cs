using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class JwtAuthenticationExtensionsTests
{
    [Fact]
    public void ConfigureBearerOptions_WhenConfigurationIsValid_UsesHardenedValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Audience"] = "MonKado.Frontend",
                ["Jwt:Issuer"] = "MonKado.Api",
                ["Jwt:SigningKey"] = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="
            })
            .Build();

        // Act
        services.AddJwtAuthentication(configuration);
        using var provider = services.BuildServiceProvider();
        var jwtOptions = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        // Assert
        var parameters = options.TokenValidationParameters;
        var signingKey = Assert.IsType<SymmetricSecurityKey>(parameters.IssuerSigningKey);

        Assert.False(options.IncludeErrorDetails);
        Assert.False(options.MapInboundClaims);
        Assert.False(options.SaveToken);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            parameters.ClockSkew);
        Assert.Equal(
            JwtRegisteredClaimNames.Sub,
            parameters.NameClaimType);
        Assert.True(parameters.RequireExpirationTime);
        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.ValidateLifetime);
        Assert.Equal(
            [SecurityAlgorithms.HmacSha256],
            parameters.ValidAlgorithms);
        Assert.Equal(
            jwtOptions.Audience,
            parameters.ValidAudience);
        Assert.Equal(
            jwtOptions.Issuer,
            parameters.ValidIssuer);
        Assert.Equal(
            Convert.FromBase64String(jwtOptions.SigningKey),
            signingKey.Key);
    }
}
