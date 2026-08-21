using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class JwtAccessTokenServiceTests
{
    private const string Audience = "MonKado.Frontend";
    private const string Issuer = "MonKado.Api";
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    private readonly DateTimeOffset _now = new(
        2026,
        8,
        21,
        12,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public void Create_WhenUserIdIsProvided_ReturnsSignedMinimalJwt()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var service = CreateService();

        // Act
        var result = service.Create(userId);

        // Assert
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);
        var claimTypes = token.Claims
            .Select(claim => claim.Type)
            .Order()
            .ToArray();

        Assert.Equal(
            JwtAccessTokenService.LifetimeSeconds,
            result.ExpiresIn);
        Assert.Equal(
            SecurityAlgorithms.HmacSha256,
            token.Header.Alg);
        Assert.Equal(
            Issuer,
            token.Issuer);
        Assert.Equal(
            Audience,
            Assert.Single(token.Audiences));
        Assert.Equal(
            _now.AddSeconds(JwtAccessTokenService.LifetimeSeconds).UtcDateTime,
            token.ValidTo);
        Assert.Equal(
            [
                JwtRegisteredClaimNames.Aud,
                JwtRegisteredClaimNames.Exp,
                JwtRegisteredClaimNames.Iat,
                JwtRegisteredClaimNames.Iss,
                JwtRegisteredClaimNames.Jti,
                JwtRegisteredClaimNames.Sub
            ],
            claimTypes);
        Assert.Equal(
            userId.ToString("D"),
            token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.NotEmpty(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value);
        Assert.Equal(
            _now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Iat).Value);
        Assert.DoesNotContain(
            token.Claims,
            claim => claim.Type is ClaimTypes.Email or ClaimTypes.Name or ClaimTypes.Role ||
                claim.Type.Contains(
                    "permission",
                    StringComparison.OrdinalIgnoreCase) ||
                claim.Type.Contains(
                    "displayName",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_WhenTokenIsValidated_AcceptsExpectedConfiguration()
    {
        // Arrange
        var service = CreateService();
        var result = service.Create(Guid.NewGuid());
        var validationParameters = CreateValidationParameters(
            Issuer,
            Audience,
            SigningKey);

        // Act
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var principal = handler.ValidateToken(
            result.Value,
            validationParameters,
            out var validatedToken);

        // Assert
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Sub));
        Assert.IsType<JwtSecurityToken>(validatedToken);
    }

    [Theory]
    [InlineData("Other.Api", Audience, SigningKey)]
    [InlineData(Issuer, "Other.Frontend", SigningKey)]
    [InlineData(Issuer, Audience, "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=")]
    public void Create_WhenTokenIsValidatedWithUnexpectedConfiguration_ThrowsSecurityTokenException(
        string issuer,
        string audience,
        string signingKey)
    {
        // Arrange
        var service = CreateService();
        var result = service.Create(Guid.NewGuid());
        var validationParameters = CreateValidationParameters(
            issuer,
            audience,
            signingKey);

        // Act
        void action() => new JwtSecurityTokenHandler().ValidateToken(
            result.Value,
            validationParameters,
            out _);

        // Assert
        Assert.ThrowsAny<SecurityTokenException>((Action)action);
    }

    private JwtAccessTokenService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Audience = Audience,
            Issuer = Issuer,
            SigningKey = SigningKey
        });

        return new JwtAccessTokenService(
            options,
            new FixedTimeProvider(_now));
    }

    private static TokenValidationParameters CreateValidationParameters(
        string issuer,
        string audience,
        string signingKey)
    {
        return new TokenValidationParameters
        {
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = false,
            ValidAudience = audience,
            ValidIssuer = issuer
        };
    }
}
