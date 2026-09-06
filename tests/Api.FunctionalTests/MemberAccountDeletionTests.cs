using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class MemberAccountDeletionTests
{
    private const string RequestPath = "/api/v1/members/current/deletion-requests";
    private readonly RecordingMemberAccountDeletionService _service = new();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenAnonymous_ReturnsUnauthorized(bool confirmation)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            confirmation ? RequestPath + "/confirm" : RequestPath,
            new
            {
                token = "confirmation"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ConfirmAsync_WhenCalledWithGet_DoesNotDeleteAccount()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            RequestPath + "/confirm?token=confirmation",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.MethodNotAllowed,
            response.StatusCode);
    }

    [Fact]
    public async Task ConfirmAsync_WhenSixthAttemptInMinute_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var invalid = await client.PostAsJsonAsync(
                RequestPath + "/confirm",
                new
                {
                    token = ""
                },
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalid.StatusCode);
        }

        // Act
        using var response = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token = ""
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
    }

    [Fact]
    public async Task OpenApiAsync_WhenRequested_DocumentsAccountDeletionContracts()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        var paths = document.GetProperty("paths");
        var request = paths
            .GetProperty(RequestPath)
            .GetProperty("post");
        Assert.True(request
                .GetProperty("responses")
                .TryGetProperty(
                "202",
                out _));
        Assert.False(request.TryGetProperty(
                "requestBody",
                out _));
        var confirm = paths
            .GetProperty(RequestPath + "/confirm")
            .GetProperty("post");
        Assert.True(confirm
                .GetProperty("requestBody")
                .GetProperty("content")
                .TryGetProperty(
                "application/json",
                out _));
        foreach (var status in new[]
        {
            "204",
            "400",
            "401",
            "429",
            "503"
        }

        )
        {
            Assert.True(confirm
                    .GetProperty("responses")
                    .TryGetProperty(
                    status,
                    out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAsync_WhenPostgreSqlFails_ServiceRetainsDependencyClassification(bool emailChange)
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var memberId = Guid.CreateVersion7();
        Func<Task> action = emailChange ? () => scope.ServiceProvider
            .GetRequiredService<IMemberEmailChangeService>()
            .RequestAsync(
            memberId,
            "new@example.test",
            "password",
            1,
            TestContext.Current.CancellationToken) : () => scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionService>()
            .RequestAsync(
            memberId,
            TestContext.Current.CancellationToken);

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);

        // Assert
        Assert.NotNull(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenAuthorized_UsesDedicatedContract(bool confirmation)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        const string Token = "protected-confirmation";
        await using var baseline = new RegistrationApiFactory();
        await using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMemberAccountDeletionService>();
                    services.AddSingleton<IMemberAccountDeletionService>(_service);
                }));
        using var client = CreateClient(
            factory,
            memberId);

        // Act
        using var response = confirmation ? await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token = Token
            },
            TestContext.Current.CancellationToken) : await client.PostAsync(
            RequestPath,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            confirmation ? HttpStatusCode.NoContent : HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        if (confirmation)
        {
            Assert.Contains(
                response.Headers.GetValues("Set-Cookie"),
                value => value.StartsWith(
                    "MonKado.Refresh=;",
                    StringComparison.Ordinal));
            Assert.Equal(
                (memberId, Token),
                Assert.Single(_service.Confirmations));
            Assert.Empty(_service.Requests);
        }
        else
        {
            Assert.Equal(
                memberId,
                Assert.Single(_service.Requests));
            Assert.Empty(_service.Confirmations);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ConfirmAsync_WhenTokenIsMissing_ReturnsValidationError(string? token)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAsync_WhenMemberCheckFails_ReturnsStructuredFailure(bool unavailable)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.MemberValidationService.Failure = unavailable ? new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException()) : new InvalidAuthenticationSessionException();
        using var client = CreateClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsync(
            RequestPath,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized,
            response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(
            (int)response.StatusCode,
            json
                .GetProperty("statusCode")
                .GetInt32());
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessToken = factory.Services
            .GetRequiredService<IAccessTokenService>()
            .Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.Value);

        return client;
    }
}
