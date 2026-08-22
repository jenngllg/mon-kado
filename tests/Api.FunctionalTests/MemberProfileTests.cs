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
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class MemberProfileTests
{
    private const string Audience = "MonKado.Frontend";
    private const string CurrentEntityTag = "\"0000002a\"";
    private const string Issuer = "MonKado.Api";
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    public async Task UpdateProfileAsync_WhenRequestIsValid_ReturnsExactProfileContractAndNewEntityTag()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        factory.MemberProfileService.MemberProfile = new MemberProfile(
            "Jenn updated",
            43);
        using var client = CreateAuthorizedClient(
            factory,
            memberId);
        using var request = CreateRequest(
            " Jenn updated ",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002b\"",
            response.Headers.ETag?.Tag);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The member profile response is empty.");
        var properties = document.RootElement.EnumerateObject().ToArray();
        var property = Assert.Single(properties);
        Assert.Equal(
            "displayName",
            property.Name);
        Assert.Equal(
            "Jenn updated",
            property.Value.GetString());
        Assert.Equal(
            [(
                memberId,
                "Jenn updated",
                42u)],
            factory.MemberProfileService.Updates);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Profile updated for member {memberId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Jenn updated",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequired()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            "Jenn",
            entityTag: null);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)StatusCodes.Status428PreconditionRequired,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestPreconditionRequired,
            error.ErrorCode);
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Theory]
    [InlineData("W/\"0000002a\"")]
    [InlineData("\"not-hex!\"")]
    [InlineData("\"0000002a\", \"0000002b\"")]
    public async Task UpdateProfileAsync_WhenIfMatchIsMalformed_ReturnsValidationError(
        string entityTag)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            "Jenn",
            entityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        var validationError = Assert.Single(error.ValidationErrors ?? []);
        Assert.Equal(
            "ifMatch",
            validationError.PropertyName);
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Jenn\nMartin")]
    public async Task UpdateProfileAsync_WhenDisplayNameIsInvalid_ReturnsValidationError(
        string? displayName)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            displayName,
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains(
            error.ValidationErrors ?? [],
            validationError => validationError.PropertyName == "displayName");
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenMemberWasDeleted_ReturnsUnauthorizedAndDeletesRefreshCookie()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var memberId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            memberId);
        using var request = CreateRequest(
            "Jenn",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
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
            [(
                memberId,
                "Jenn",
                42u)],
            factory.MemberProfileService.Updates);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenVersionIsStale_ReturnsPreconditionFailed()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberProfileService.Exception = new MemberProfileVersionConflictException();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            "Jenn",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.MemberProfileVersionConflict,
            error.ErrorCode);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberProfileService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateRequest(
            "Jenn",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenBearerIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "Jenn",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task UpdateProfileAsync_WhenSubjectIsInvalid_ReturnsInvalidAuthenticationSession(
        string? subject)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            CreateSignedToken(subject));
        using var request = CreateRequest(
            "Jenn",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
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
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenSubjectAndRequestAreInvalid_ReturnsAuthenticationErrorFirst()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            CreateSignedToken("invalid"));
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile")
        {
            Content = new StringContent(
                "displayName=Jenn",
                Encoding.UTF8,
                "text/plain")
        };

        // Act
        using var response = await client.SendAsync(
            request,
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
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile")
        {
            Content = new StringContent(
                "displayName=Jenn",
                Encoding.UTF8,
                "text/plain")
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestUnsupportedMediaType,
            error.ErrorCode);
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile")
        {
            Content = new StringContent(
                "{\"displayName\":\"" + new string(
                    'a',
                    5 * 1024) + "\"}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            CurrentEntityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestPayloadTooLarge,
            error.ErrorCode);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.MemberProfileService.Updates);
    }

    private static HttpClient CreateAuthorizedClient(
        RegistrationApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static HttpRequestMessage CreateRequest(
        string? displayName,
        string? entityTag)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile")
        {
            Content = JsonContent.Create(new { displayName })
        };

        if (entityTag is not null)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);

        return request;
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
