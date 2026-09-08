using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class AdministrativeAuditTests
{
    private const string Route = "/api/v1/admin/audit-events";
    private readonly Mock<IAdministrativeAuditService> _serviceMock;
    private readonly Mock<IAdministratorAccessService> _accessMock;
    private readonly Guid _administratorId = Guid.CreateVersion7();

    public AdministrativeAuditTests()
    {
        _serviceMock = new Mock<IAdministrativeAuditService>(MockBehavior.Strict);
        _accessMock = new Mock<IAdministratorAccessService>(MockBehavior.Strict);
        _accessMock
            .Setup(service => service.GetAccessAsync(
                _administratorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdministratorAccess.Granted);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=", "page")]
    [InlineData("pageSize=%20", "pageSize")]
    [InlineData("administratorId=", "administratorId")]
    [InlineData("memberId=", "memberId")]
    [InlineData("wishlistId=", "wishlistId")]
    [InlineData("exportId=", "exportId")]
    [InlineData("action=", "action")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("administratorId=00000000-0000-0000-0000-000000000000", "administratorId")]
    [InlineData("memberId=00000000-0000-0000-0000-000000000000", "memberId")]
    [InlineData("wishlistId=00000000-0000-0000-0000-000000000000", "wishlistId")]
    [InlineData("exportId=00000000-0000-0000-0000-000000000000", "exportId")]
    [InlineData("action=unknown", "action")]
    [InlineData("action=99", "action")]
    [InlineData("from=", "from")]
    [InlineData("to=", "to")]
    [InlineData("from=2026-09-01T12:00:00.Z", "from")]
    [InlineData("to=2026-09-01T12:00:00.%2B02:00", "to")]
    [InlineData("from=bad&to=2026-09-01T12:00:00Z", "from")]
    [InlineData("from=2026-09-01T12:00:00Z&to=bad", "to")]
    [InlineData("from=2026-09-01T12:00:00", "from")]
    [InlineData("to=2026-09-01T12:00:00", "to")]
    [InlineData("from=2026-09-01T12:00:00Z&to=2026-09-01T12:00:00Z", "to")]
    [InlineData("from=2026-09-02T12:00:00Z&to=2026-09-01T12:00:00Z", "to")]
    [InlineData("requestReference=", "requestReference")]
    [InlineData("requestReference=%20%20", "requestReference")]
    [InlineData("requestReference=SUPPORT%0A806", "requestReference")]
    [InlineData("longReference", "requestReference")]
    public async Task GetPageAsync_WhenInputIsInvalid_ReturnsStructuredValidationWithoutReading(
        string query,
        string property)
    {
        // Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        if (query == "longReference")
            query = "requestReference=" + new string(
                'a',
                129);

        // Act
        using var response = await client.GetAsync(
            Route + "?" + query,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            400,
            body.GetProperty("statusCode")
                .GetInt32());
        Assert.Contains(
            body.GetProperty("validationErrors")
                .EnumerateArray(),
            error => error.GetProperty("propertyName")
                .GetString() == property);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPageAsync_WhenDependencySucceedsOrFails_PreservesPrivateHeadersAndLogs(bool unavailable)
    {
        // Arrange
        var setup = _serviceMock.Setup(service => service.GetPageAsync(
            It.Is<AdministrativeAuditFilter>(filter => filter.RequestReference == "PRIVATE-REFERENCE"),
            It.IsAny<CancellationToken>()));

        if (unavailable)
            setup.ThrowsAsync(new DependencyUnavailableException(
                "PostgreSQL",
                null));
        else
            setup.ReturnsAsync(new AdministrativeAuditPage
            {
                CurrentPage = 1,
                PageSize = 20,
                TotalCount = 0
            });
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            Route + "?requestReference=PRIVATE-REFERENCE",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        if (unavailable)
            Assert.Equal(
                "TECHNICAL_DEPENDENCY_UNAVAILABLE",
                body.GetProperty("errorCode")
                    .GetString());
        else
            Assert.Empty(body.GetProperty("items")
                .EnumerateArray());
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "PRIVATE-REFERENCE",
                StringComparison.Ordinal));
        _serviceMock.Verify(
            service => service.GetPageAsync(
                It.Is<AdministrativeAuditFilter>(filter => filter.RequestReference == "PRIVATE-REFERENCE"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Fact]
    public async Task OpenApiAsync_WhenRequested_DescribesFiltersActionsNullsAndBearerOnlyRead()
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
            .GetProperty(Route)
            .GetProperty("get");
        foreach (var status in new[]
        {
            "200",
            "400",
            "401",
            "403",
            "500",
            "503"
        })
        {
            Assert.True(operation.GetProperty("responses")
                .TryGetProperty(
                    status,
                    out _));
        }
        Assert.False(operation.TryGetProperty(
            "requestBody",
            out _));
        Assert.NotEmpty(operation.GetProperty("security")
            .EnumerateArray());
        var parameters = operation.GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            [
                "action",
                "administratorId",
                "exportId",
                "from",
                "memberId",
                "page",
                "pageSize",
                "requestReference",
                "to",
                "wishlistId"
            ],
            parameters
                .Where(parameter => parameter.GetProperty("in")
                    .GetString() == "query")
                .Select(parameter => parameter.GetProperty("name")
                    .GetString())
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.GetProperty("name")
                .GetString() is "If-Match" or "X-CSRF-TOKEN");
        var schemas = document.GetProperty("components")
            .GetProperty("schemas");
        var properties = schemas.GetProperty("AdministrativeAuditEventResponse")
            .GetProperty("properties");
        Assert.Equal(
            10,
            properties.EnumerateObject()
                .Count());
        foreach (var name in new[]
        {
            "administratorId",
            "administratorDisplayName",
            "wishlistId",
            "memberId",
            "exportId",
            "reason",
            "requestReference"
        })
        {
            Assert.Contains(
                "null",
                properties.GetProperty(name)
                    .GetProperty("type")
                    .EnumerateArray()
                    .Select(type => type.GetString()));
        }
        Assert.Equal(
            [
                "wishlistSuspended",
                "wishlistSuspensionReasonUpdated",
                "wishlistReactivated",
                "memberDataExportRequested",
                "memberDataExportDownloadStarted",
                "memberErased"
            ],
            schemas.GetProperty("AdministrativeAuditAction")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        _serviceMock.VerifyNoOtherCalls();
        _accessMock.VerifyNoOtherCalls();
    }

    private AdministrativeAuditApiFactory CreateFactory()
    {

        return new AdministrativeAuditApiFactory(
            _serviceMock.Object,
            _accessMock.Object);
    }

    private HttpClient CreateClient(AdministrativeAuditApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.Services.GetRequiredService<IAccessTokenService>()
                .Create(_administratorId)
                .Value);

        return client;
    }

    private void VerifyAuthorization()
    {
        _accessMock.Verify(
            service => service.GetAccessAsync(
                _administratorId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _accessMock.VerifyNoOtherCalls();
    }
}
