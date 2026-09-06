using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class ProfileImageTests
{
    private const string Route = "/api/v1/members/current/profile/image";
    private const string EntityTag = "\"0000002a\"";
    [Theory]
    [InlineData("missing", 400)]
    [InlineData("empty", 400)]
    [InlineData("wrong-name", 400)]
    [InlineData("wrong-case", 400)]
    [InlineData("text", 400)]
    [InlineData("duplicate", 400)]
    [InlineData("corrupt", 400)]
    [InlineData("unsupported", 415)]
    [InlineData("oversized", 413)]
    [InlineData("json", 415)]
    public async Task UpsertAsync_WhenInputIsInvalid_ReturnsStructuredErrorWithoutPersistence(
        string scenario,
        int expectedStatus)
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        using var client = CreateClient(factory);
        using var request = CreateUploadRequest(scenario);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            (int)response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            expectedStatus,
            error.StatusCode);
        Assert.NotNull(error.ErrorCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            0,
            factory.ProfileImageService.UpsertCount);
    }

    [Theory]
    [InlineData("PUT", null, 428)]
    [InlineData("PUT", "invalid", 400)]
    [InlineData("DELETE", null, 428)]
    [InlineData("DELETE", "invalid", 400)]
    public async Task ChangeAsync_WhenPreconditionIsMissingOrMalformed_RejectsRequest(
        string method,
        string? entityTag,
        int expectedStatus)
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        using var client = CreateClient(factory);
        using var request = CreateUploadRequest("valid");
        request.Method = new HttpMethod(method);
        request.Headers.Remove("If-Match");

        if (entityTag is not null)
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ChangeAsync_WhenBearerIsAbsent_RejectsAnonymousAndCookieOnlyRequests(string method)
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateUploadRequest("valid");
        request.Method = new HttpMethod(method);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            "MonKado.Refresh=not-a-bearer-token");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task UpsertAsync_WhenMemberExceedsUploadQuota_RejectsEleventhUploadWithoutBlockingDeletion()
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        using var client = CreateClient(factory);
        for (var index = 0; index < 10; index++)
        {
            using var upload = CreateUploadRequest("valid");
            using var uploaded = await client.SendAsync(
                upload,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.OK,
                uploaded.StatusCode);
        }

        // Act
        using var extraUpload = CreateUploadRequest("valid");
        using var response = await client.SendAsync(
            extraUpload,
            TestContext.Current.CancellationToken);
        using var deletionRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            Route);
        deletionRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTag);
        using var deletion = await client.SendAsync(
            deletionRequest,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(
            10,
            factory.ProfileImageService.UpsertCount);
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletion.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ChangeAsync_WhenPostgreSqlIsUnavailable_ReturnsStructured503(string method)
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        factory.ProfileImageService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateClient(factory);
        using var request = CreateUploadRequest("valid");
        request.Method = new HttpMethod(method);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty-member")]
    [InlineData("wrong-image")]
    public async Task GetAsync_WhenPublicReferenceIsInvalid_Returns404(string scenario)
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        using var client = factory.CreateClient();
        var memberId = scenario == "empty-member" ? Guid.Empty : Guid.CreateVersion7();
        var url = $"/api/v1/members/{memberId}/profile/image";

        if (scenario != "missing")
            url += $"?imageId={Guid.CreateVersion7()}";

        // Act
        using var response = await client.GetAsync(
            url,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.ProfileImageNotFound,
            error.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_WhenReferencedFileIsMissing_Returns503WithoutDisclosingStoragePath()
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        using var client = CreateClient(factory);
        using var uploadRequest = CreateUploadRequest("valid");
        using var upload = await client.SendAsync(
            uploadRequest,
            TestContext.Current.CancellationToken);
        var profile = await upload.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var url = Assert.IsType<string>(profile
                .GetProperty("profileImageUrl")
                .GetString());
        await factory.Services
            .GetRequiredService<IGiftImageStore>()
            .DeleteAsync(
            factory.ProfileImageService.ImageId.GetValueOrDefault(),
            TestContext.Current.CancellationToken);

        // Act
        using var response = await client.GetAsync(
            url,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var logs = string.Join(
            '\n',
            factory.LogMessages);
        Assert.DoesNotContain(
            factory.StoragePath,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private-original-name.png",
            logs,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_WhenAuthenticatedMemberIdIsMissing_StopsAtValidation(bool deleting)
    {
        // Arrange
        await using var factory = new ProfileImageApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Act
        var action = async () =>
        {

            if (deleting)
            {
                await sender.Send(
                    new DeleteProfileImageCommand(
                        Guid.Empty,
                        42),
                    TestContext.Current.CancellationToken);

                return;
            }

            await sender.Send(
                new UpsertProfileImageCommand(
                    Guid.Empty,
                    [1],
                    42,
                    true),
                TestContext.Current.CancellationToken);
        };

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        Assert.Equal(
            0,
            factory.ProfileImageService.UpsertCount);
    }

    private static HttpClient CreateClient(ProfileImageApiFactory factory)
    {
        var client = factory.CreateClient();
        var token = factory.Services
            .GetRequiredService<IAccessTokenService>()
            .Create(Guid.CreateVersion7());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.Value);

        return client;
    }

    private static HttpRequestMessage CreateUploadRequest(string scenario)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            Route);
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            EntityTag);

        if (scenario == "json")
        {
            request.Content = JsonContent.Create(new
            {
                image = "invalid"
            });

            return request;
        }

        var multipart = new MultipartFormDataContent();
        request.Content = multipart;

        if (scenario == "missing")
            return request;
        var content = scenario switch
        {
            "empty" => [],
            "corrupt" => new byte[]
            {
                0xff,
                0xd8,
                0xff,
                0xe0
            },
            "unsupported" => "GIF89a"u8.ToArray(),
            "oversized" => new byte[GiftImageConstraints.MaximumInputLength + 1],
            _ => CreatePng()
        };
        var fieldName = scenario switch
        {
            "wrong-name" => "photo",
            "wrong-case" => "Image",
            _ => "image"
        };
        multipart.Add(
            new ByteArrayContent(content),
            fieldName,
            "private-original-name.png");

        if (scenario == "text")
            multipart.Add(
                new StringContent("private-field-value"),
                "extra");

        if (scenario == "duplicate")
            multipart.Add(
                new ByteArrayContent(content),
                "image",
                "another.png");

        return request;
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(
            4,
            4);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        return data.ToArray();
    }
}
