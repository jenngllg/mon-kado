using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class CurrentSessionTests
{
    private const string Audience = "MonKado.Frontend";
    private const string Issuer = "MonKado.Api";
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    public async Task GetCurrentAsync_WhenBearerIsValid_ReturnsExactCurrentSessionContract()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        factory.CurrentSessionService.CurrentSession = new CurrentSession(
            memberId,
            "jenn@example.fr",
            "Jenn",
            [
                "Administrator",
                "Member"
            ]);
        using var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The current session response is empty.");
        var payload = document.RootElement;
        var propertyNames = payload
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(property => property)
            .ToArray();
        Assert.Equal(
            [
                "displayName",
                "email",
                "id",
                "roles"
            ],
            propertyNames);
        Assert.Equal(
            memberId,
            payload.GetProperty("id").GetGuid());
        Assert.Equal(
            "jenn@example.fr",
            payload.GetProperty("email").GetString());
        Assert.Equal(
            "Jenn",
            payload.GetProperty("displayName").GetString());
        Assert.Equal(
            [
                "Administrator",
                "Member"
            ],
            payload
                .GetProperty("roles")
                .EnumerateArray()
                .Select(role => role.GetString() ?? string.Empty)
                .ToArray());
        Assert.Equal(
            [memberId],
            factory.CurrentSessionService.MemberIds);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Current session retrieved for member {memberId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "jenn@example.fr",
                StringComparison.Ordinal) || message.Contains(
                    "Administrator",
                    StringComparison.Ordinal) || message.Contains(
                        "Member",
                        StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCurrentAsync_WhenBearerIsMissing_ReturnsStructuredUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.SecurityUnauthorized,
            error.ErrorCode);
        Assert.Empty(factory.CurrentSessionService.MemberIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetCurrentAsync_WhenSubjectIsInvalid_ReturnsInvalidAuthenticationSession(
        string? subject)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            CreateSignedToken(subject));

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        Assert.Empty(factory.CurrentSessionService.MemberIds);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenMemberWasDeleted_ReturnsUnauthorizedAndDeletesRefreshCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        using var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        Assert.Equal(
            [memberId],
            factory.CurrentSessionService.MemberIds);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailableAndPreservesRefreshCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        factory.CurrentSessionService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies) && cookies.Any(value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal)));
        Assert.Equal(
            [memberId],
            factory.CurrentSessionService.MemberIds);
    }

    private static string CreateSignedToken(string? subject)
    {
        var claims = subject is null
            ? []
            : new[]
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    subject)
            };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            notBefore: null,
            expires: DateTime.UtcNow.AddMinutes(5),
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
