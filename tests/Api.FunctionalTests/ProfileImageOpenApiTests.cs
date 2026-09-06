using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class ProfileImageOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenProfileImagesAreDocumented_ExposesMultipartPreconditionsAndPublicRead()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var paths = document.GetProperty("paths");
        var current = paths.GetProperty("/api/v1/members/current/profile/image");
        var upload = current.GetProperty("put");
        var deletion = current.GetProperty("delete");
        var read = paths
            .GetProperty("/api/v1/members/{memberId}/profile/image")
            .GetProperty("get");

        // Assert
        var protectedOperations = new[]
        {
            upload,
            deletion
        };
        foreach (var operation in protectedOperations)
        {
            Assert.True(Assert
                    .Single(operation
                        .GetProperty("security")
                        .EnumerateArray())
                    .TryGetProperty(
                    "Bearer",
                    out _));
            var parameters = operation
                .GetProperty("parameters")
                .EnumerateArray();
            Assert.True(Assert
                    .Single(
                    parameters,
                    parameter => parameter
                        .GetProperty("name")
                        .GetString() == "If-Match")
                    .GetProperty("required")
                    .GetBoolean());
            Assert.DoesNotContain(
                operation
                    .GetProperty("parameters")
                    .EnumerateArray(),
                parameter => parameter
                    .GetProperty("name")
                    .GetString() == "X-CSRF-TOKEN");
        }

        Assert.False(deletion.TryGetProperty(
                "requestBody",
                out _));
        var multipart = upload
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");
        Assert.Equal(
            "image",
            Assert
                .Single(multipart
                    .GetProperty("required")
                    .EnumerateArray())
                .GetString());
        var imageProperty = Assert.Single(multipart
                .GetProperty("properties")
                .EnumerateObject());
        Assert.Equal(
            "image",
            imageProperty.Name);
        Assert.Equal(
            "binary",
            imageProperty.Value
                .GetProperty("format")
                .GetString());
        AssertStatuses(
            upload,
            [
                "200",
                "400",
                "401",
                "403",
                "412",
                "413",
                "415",
                "428",
                "429",
                "500",
                "503"
            ]);
        AssertStatuses(
            deletion,
            [
                "204",
                "400",
                "401",
                "403",
                "404",
                "412",
                "428",
                "429",
                "500",
                "503"
            ]);
        AssertStatuses(
            read,
            [
                "200",
                "400",
                "404",
                "429",
                "500",
                "503"
            ]);
        Assert.False(read.TryGetProperty(
                "security",
                out var security) && security.GetArrayLength() > 0);
        Assert.True(read
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .TryGetProperty(
                "image/webp",
                out _));
        var mutationResponses = new[]
        {
            upload
                .GetProperty("responses")
                .GetProperty("200"),
            deletion
                .GetProperty("responses")
                .GetProperty("204")
        };
        foreach (var response in mutationResponses)
        {
            Assert.True(response
                    .GetProperty("headers")
                    .TryGetProperty(
                    "Cache-Control",
                    out _));
            Assert.True(response
                    .GetProperty("headers")
                    .TryGetProperty(
                    "ETag",
                    out _));
        }
    }

    private static void AssertStatuses(
        JsonElement operation,
        string[] statuses)
    {
        Assert.Equal(
            statuses,
            operation
                .GetProperty("responses")
                .EnumerateObject()
                .Select(response => response.Name)
                .Order());
    }
}
