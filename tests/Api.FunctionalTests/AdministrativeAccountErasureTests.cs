using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class AdministrativeAccountErasureTests
{
    private readonly Mock<IAdministrativeAccountErasureService> _serviceMock;
    private readonly Mock<IAdministratorAccessService> _accessMock;
    private readonly Guid _administratorId = Guid.CreateVersion7();
    private readonly Guid _memberId = Guid.CreateVersion7();

    public AdministrativeAccountErasureTests()
    {
        _serviceMock = new Mock<IAdministrativeAccountErasureService>(MockBehavior.Strict);
        _accessMock = new Mock<IAdministratorAccessService>(MockBehavior.Strict);
        _accessMock.Setup(service => service.GetAccessAsync(
                _administratorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdministratorAccess.Granted);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfirmed_ReturnsNoContentWithoutCookiesOrReferenceInLogs()
    {
        // Arrange
        _serviceMock.Setup(service => service.ExecuteAsync(
                _administratorId,
                _memberId,
                "SUPPORT-808",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.CreateVersion7());
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(),
            new
            {
                requestReference = "  SUPPORT-808  ",
                confirmedMemberId = _memberId
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains("SUPPORT-808"));
        _serviceMock.Verify(service => service.ExecuteAsync(
                _administratorId,
                _memberId,
                "SUPPORT-808",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    [InlineData("control")]
    [InlineData("length")]
    public async Task ExecuteAsync_WhenInputIsInvalid_ReturnsCentralizedValidationWithoutCallingService(string scenario)
    {
        // Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var reference = scenario switch
        {
            "control" => "SUPPORT-808\n",
            "length" => new string(
                'a',
                129),
            "missing" => null,
            _ => "SUPPORT-808"
        };
        var confirmedMemberId = scenario == "mismatch" ? Guid.CreateVersion7() : _memberId;

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(),
            new
            {
                requestReference = reference,
                confirmedMemberId
            },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            400,
            body.GetProperty("statusCode").GetInt32());
        Assert.Contains(
            body.GetProperty("validationErrors").EnumerateArray(),
            error => error.GetProperty("propertyName").GetString() == (scenario == "mismatch" ? "confirmedMemberId" : "requestReference"));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, "ACCOUNT_ERASURE_TARGET_NOT_FOUND")]
    [InlineData("self", HttpStatusCode.Conflict, "ACCOUNT_SELF_ERASURE_NOT_ALLOWED")]
    [InlineData("database", HttpStatusCode.ServiceUnavailable, "TECHNICAL_DEPENDENCY_UNAVAILABLE")]
    public async Task ExecuteAsync_WhenServiceRejects_ReturnsStructuredFailureWithoutChangingCookies(
        string scenario,
        HttpStatusCode status,
        string errorCode)
    {
        // Arrange
        Exception failure = scenario switch
        {
            "missing" => new AccountErasureTargetNotFoundException(),
            "self" => new AccountSelfErasureNotAllowedException(),
            _ => new DependencyUnavailableException(
                "PostgreSQL",
                new TimeoutException())
        };
        _serviceMock.Setup(service => service.ExecuteAsync(
                _administratorId,
                _memberId,
                "SUPPORT-808",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = _memberId
            },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            status,
            response.StatusCode);
        Assert.Equal(
            errorCode,
            body.GetProperty("errorCode").GetString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        _serviceMock.Verify(service => service.ExecuteAsync(
                _administratorId,
                _memberId,
                "SUPPORT-808",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Theory]
    [InlineData(false, HttpStatusCode.UnsupportedMediaType)]
    [InlineData(true, HttpStatusCode.RequestEntityTooLarge)]
    public async Task ExecuteAsync_WhenBodyContractIsRejected_PreservesCookiesAndDoesNotErase(
        bool oversized,
        HttpStatusCode expectedStatus)
    {
        // Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var content = new StringContent(
            oversized ? new string(
                'a',
                5 * 1024) : "not-json",
            Encoding.UTF8,
            oversized ? "application/json" : "text/plain");

        // Act
        using var response = await client.PostAsync(
            GetRoute(),
            content,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        Assert.Equal(
            (int)expectedStatus,
            body.GetProperty("statusCode").GetInt32());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        _serviceMock.VerifyNoOtherCalls();
        _accessMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenQuotaIsExceeded_RejectsBeforeErasureAndPreservesCookies()
    {
        // Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var permitLimit = AuthenticationRateLimitingExtensions.AdministrativeAccountErasurePermitLimit;

        // Act
        for (var attempt = 0; attempt < permitLimit; attempt++)
        {
            using var invalid = await client.PostAsJsonAsync(
                GetRoute(),
                new
                {
                    requestReference = "",
                    confirmedMemberId = _memberId
                },
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalid.StatusCode);
        }

        using var response = await client.PostAsJsonAsync(
            GetRoute(),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = _memberId
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        _serviceMock.VerifyNoOtherCalls();
        _accessMock.Verify(service => service.GetAccessAsync(
                _administratorId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(permitLimit));
        _accessMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpenApiAsync_WhenRequested_DocumentsDestructiveBearerContract()
    {
        // Arrange
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        var operation = document.GetProperty("paths")
            .GetProperty("/api/v1/admin/members/{memberId}/erasure-requests")
            .GetProperty("post");
        foreach (var status in new[]
        {
            "204",
            "400",
            "401",
            "403",
            "404",
            "409",
            "413",
            "415",
            "429",
            "500",
            "503"
        })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(
                status,
                out _));
        }

        var request = document.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ExecuteAdministrativeAccountErasureRequest")
            .GetProperty("properties");
        Assert.Equal(
            [
                "confirmedMemberId",
                "requestReference"
            ],
            request.EnumerateObject()
                .Select(property => property.Name)
                .Order());
        Assert.False(operation.GetProperty("responses").GetProperty("204").TryGetProperty(
            "content",
            out _));
        Assert.NotEmpty(operation.GetProperty("security").EnumerateArray());
        _serviceMock.VerifyNoOtherCalls();
        _accessMock.VerifyNoOtherCalls();
    }

    private AdministrativeAccountErasureApiFactory CreateFactory()
    {

        return new AdministrativeAccountErasureApiFactory(
            _serviceMock.Object,
            _accessMock.Object);
    }

    private HttpClient CreateClient(AdministrativeAccountErasureApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.Services.GetRequiredService<IAccessTokenService>()
                .Create(_administratorId)
                .Value);

        return client;
    }

    private string GetRoute()
    {

        return $"/api/v1/admin/members/{_memberId:D}/erasure-requests";
    }

    private void VerifyAuthorization()
    {
        _accessMock.Verify(service => service.GetAccessAsync(
                _administratorId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _accessMock.VerifyNoOtherCalls();
    }
}
