using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;

using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class AntiforgeryErrorResponseTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("altered")]
    [InlineData("identity")]
    public async Task PostAsync_WhenAntiforgeryIsRejected_ReturnsExactErrorContract(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            cancellationToken);
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/_tests/security/mutate");

        if (scenario != "missing")
            request.Headers.Add(
                WebSecurityOptions.AntiforgeryHeaderName,
                scenario == "altered" ? "invalid-token" : csrf.Token);

        if (scenario == "identity")
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateToken());

        // Act
        using var response = await client.SendAsync(
            request,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            [
                "errorCode",
                "message",
                "statusCode",
                "title",
                "validationErrors"
            ],
            body.EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
        Assert.Equal(
            400,
            body.GetProperty("statusCode").GetInt32());
        Assert.Equal(
            ErrorCodes.SecurityCsrfValidationFailed,
            body.GetProperty("errorCode").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("validationErrors").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostAsync_WhenAntiforgeryMatchesIdentity_ExecutesAction(bool authenticated)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateToken());
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            cancellationToken);
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/_tests/security/mutate");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrf.Token);

        // Act
        using var response = await client.SendAsync(
            request,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
    }

    private static string CreateToken()
    {
        var token = new JwtSecurityToken(
            SecurityApiFactory.JwtIssuer,
            SecurityApiFactory.JwtAudience,
            [
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    Guid.CreateVersion7().ToString()),
                new Claim(
                    JwtRegisteredClaimNames.Jti,
                    Guid.CreateVersion7().ToString("N"))
            ],
            expires: TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(SecurityApiFactory.JwtSigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
