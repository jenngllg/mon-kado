using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistReportOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenWishlistReportIsDocumented_ExposesAnonymousContract()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI document is empty.");
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/reports")
            .GetProperty("post");
        var schemaReference = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();
        var reasonSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ReportSharedWishlistRequest")
            .GetProperty("properties")
            .GetProperty("reason");

        // Assert
        Assert.Equal(
            "Reports a wishlist through its active share link.",
            operation.GetProperty("summary").GetString());
        Assert.Equal(
            "#/components/schemas/ReportSharedWishlistRequest",
            schemaReference);
        Assert.DoesNotContain(
            "security",
            operation.EnumerateObject().Select(property => property.Name));
        var parameters = operation.GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => ResolveParameter(
                document.RootElement,
                parameter))
            .ToArray();
        var shareToken = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-MonKado-Share-Token");
        Assert.Equal(
            "header",
            shareToken.GetProperty("in").GetString());
        var antiforgeryToken = Assert.Single(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.Equal(
            "header",
            antiforgeryToken.GetProperty("in").GetString());
        Assert.True(antiforgeryToken.GetProperty("required").GetBoolean());
        Assert.Equal(
            [
                "spamOrScam",
                "inappropriateContent",
                "privacyViolation",
                "other"
            ],
            GetEnumValues(
                document.RootElement,
                reasonSchema));
        Assert.Equal(
            [
                "204",
                "400",
                "404",
                "413",
                "415",
                "429",
                "503",
                "500"
            ],
            operation.GetProperty("responses")
                .EnumerateObject()
                .Select(response => response.Name));
    }

    private static string[] GetEnumValues(
        JsonElement document,
        JsonElement schema)
    {
        var target = schema;

        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            target = anyOf
                .EnumerateArray()
                .Single(candidate => candidate.TryGetProperty("$ref", out _));
        }

        if (target.TryGetProperty("$ref", out var reference))
        {
            var schemaName = reference.GetString()?.Split('/').Last()
                ?? throw new InvalidOperationException("The enum schema reference is empty.");
            target = document
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(schemaName);
        }

        if (!target.TryGetProperty(
                "enum",
                out var values))
        {

            throw new InvalidOperationException(
                $"The enum schema is missing values: {target.GetRawText()}");
        }

        return values
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
    }

    private static JsonElement ResolveParameter(
        JsonElement document,
        JsonElement parameter)
    {

        if (!parameter.TryGetProperty(
                "$ref",
                out var reference))
            return parameter;

        var parameterName = reference.GetString()?.Split('/').Last()
            ?? throw new InvalidOperationException("The parameter reference is empty.");

        return document
            .GetProperty("components")
            .GetProperty("parameters")
            .GetProperty(parameterName);
    }
}
