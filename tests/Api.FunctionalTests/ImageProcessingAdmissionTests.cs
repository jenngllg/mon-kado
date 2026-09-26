using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class ImageProcessingAdmissionTests
{
    [Theory]
    [InlineData("PUT", "/api/v1/members/current/profile/image")]
    [InlineData("PUT", "/api/v1/wishlists/01950000-0000-7000-8000-000000000001/wishes/01950000-0000-7000-8000-000000000002/image")]
    [InlineData("POST", "/api/v1/wishlists/01950000-0000-7000-8000-000000000001/wish-import-previews")]
    public async Task UploadAsync_WhenSharedAdmissionIsFull_RejectsBeforeBodySizeAndModelBinding(
        string method,
        string route)
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var frozenFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new FrozenTimerTimeProvider())));
        using var client = frozenFactory.CreateClient();
        var limiter = frozenFactory.Services.GetRequiredService<IImageProcessingLimiter>();
        using var occupied = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        using var queuedCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var queuedFirst = limiter.AcquireAsync(queuedCancellation.Token).AsTask();
        var queuedSecond = limiter.AcquireAsync(queuedCancellation.Token).AsTask();
        var token = frozenFactory.Services.GetRequiredService<IAccessTokenService>()
            .Create(Guid.CreateVersion7())
            .Value;
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            route);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        request.Content = new ByteArrayContent([]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(method == "POST" ? "application/json" : "multipart/form-data");
        request.Content.Headers.ContentLength = 20 * 1024 * 1024;

        try
        {
            // Act
            using var response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            using var read = await client.GetAsync(
                "/security/csrf-token",
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.Equal(
                "REQUEST_RATE_LIMIT_EXCEEDED",
                body.RootElement.GetProperty("errorCode")
                    .GetString());
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(
                TimeSpan.FromSeconds(60),
                response.Headers.RetryAfter?.Delta);
            Assert.Equal(
                HttpStatusCode.OK,
                read.StatusCode);
            Assert.False(queuedFirst.IsCompleted);
            Assert.False(queuedSecond.IsCompleted);
        }
        finally
        {
            await queuedCancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedFirst);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedSecond);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-bearer")]
    public async Task UploadAsync_WhenNotAuthenticated_DoesNotWaitForAdmission(string? bearer)
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var frozenFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new FrozenTimerTimeProvider())));
        using var client = frozenFactory.CreateClient();
        var limiter = frozenFactory.Services.GetRequiredService<IImageProcessingLimiter>();
        using var occupied = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile/image");
        request.Content = new MultipartFormDataContent();

        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                bearer);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }
}
