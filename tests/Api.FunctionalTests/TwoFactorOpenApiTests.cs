using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class TwoFactorOpenApiTests
{
    [Theory]
    [InlineData(
        "/api/v1/auth/two-factor/completions",
        "post",
        false,
        true,
        "AccessTokenResponse")]
    [InlineData(
        "/api/v1/auth/two-factor/setup",
        "post",
        false,
        true,
        "TwoFactorSetupResponse")]
    [InlineData(
        "/api/v1/auth/two-factor/setup/confirmations",
        "post",
        false,
        true,
        "TwoFactorRecoveryCodesResponse")]
    [InlineData(
        "/api/v1/auth/two-factor/recovery-codes/regenerations",
        "post",
        true,
        false,
        "TwoFactorRecoveryCodesResponse")]
    [InlineData(
        "/api/v1/members/current/two-factor",
        "get",
        true,
        false,
        "TwoFactorStatusResponse")]
    [InlineData(
        "/api/v1/members/current/two-factor/reauthentications",
        "post",
        true,
        false,
        "TwoFactorChallengeResponse")]
    public async Task GetAsync_WhenTwoFactorOperationIsDocumented_ExposesSecurityAndResponseContract(
        string path,
        string method,
        bool requiresBearer,
        bool requiresAntiforgery,
        string responseType)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var operation = document
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);

        // Assert
        Assert.Equal(
            requiresBearer,
            operation.TryGetProperty(
                "security",
                out var security) && security
                .EnumerateArray()
                .Any(requirement => requirement.TryGetProperty(
                    "Bearer",
                    out _)));
        var parameters = operation.TryGetProperty(
            "parameters",
            out var parameterArray)
            ? parameterArray.EnumerateArray().ToArray()
            : [];
        Assert.Equal(
            requiresAntiforgery,
            parameters.Any(parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN" &&
                parameter.GetProperty("required").GetBoolean()));
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.GetProperty("name").GetString() is "flow" or "If-Match");
        var responses = operation.GetProperty("responses");
        var success = responses.GetProperty("200");
        Assert.Equal(
            $"#/components/schemas/{responseType}",
            success
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Contains(
            "no-store",
            success
                .GetProperty("headers")
                .GetProperty("Cache-Control")
                .GetProperty("description")
                .GetString());
        Assert.True(responses.TryGetProperty(
            "401",
            out _));
        Assert.True(responses.TryGetProperty(
            "503",
            out _));
        Assert.True(responses.TryGetProperty(
            "500",
            out _));
    }

    [Theory]
    [InlineData("/api/v1/auth/sessions")]
    [InlineData("/api/v1/auth/google/completions")]
    [InlineData("/api/v1/auth/google/link")]
    [InlineData("/api/v1/auth/two-factor/completions")]
    public async Task GetAsync_WhenSignInRequiresTwoFactor_ExposesAcceptedChallengeWithoutTokens(string path)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var accepted = document
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("202");

        // Assert
        Assert.Equal(
            "#/components/schemas/TwoFactorChallengeResponse",
            accepted
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Contains(
            "no-store",
            accepted
                .GetProperty("headers")
                .GetProperty("Cache-Control")
                .GetProperty("description")
                .GetString());
        var schema = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("TwoFactorChallengeResponse");
        Assert.Equal(
            [
                "expiresAt",
                "flow",
                "requiredAction"
            ],
            schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
    }
}
