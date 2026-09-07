using JennGllg.Fr.MonKado.Back.Api.Errors;
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

public class PersonalDataExportTests
{
    private const string Route = "/api/v1/members/current/data-exports";
    private readonly Mock<IPersonalDataExportService> _serviceMock;
    private readonly Guid _memberId = Guid.CreateVersion7();
    public PersonalDataExportTests()
    {
        _serviceMock = new Mock<IPersonalDataExportService>(MockBehavior.Strict);
    }

    [Theory]
    [InlineData(PersonalDataExportStatus.Queued, HttpStatusCode.Accepted)]
    [InlineData(PersonalDataExportStatus.Processing, HttpStatusCode.Accepted)]
    [InlineData(PersonalDataExportStatus.Ready, HttpStatusCode.OK)]
    public async Task RequestAsync_WhenAuthenticated_ReturnsExactMetadataWithoutChangingCookies(
        PersonalDataExportStatus status,
        HttpStatusCode expectedStatus)
    {
        // Arrange
        var details = CreateDetails(
            status,
            null);
        CancellationToken receivedToken = default;
        _serviceMock
            .Setup(service => service.RequestAsync(
                _memberId,
                It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((
                _,
                token) => receivedToken = token)
            .ReturnsAsync(details);
        await using var factory = new PersonalDataExportApiFactory(_serviceMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        Assert.Equal(
            $"{Route}/{details.Id:D}",
            response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            [
                "id",
                "status",
                "createdAt",
                "snapshotAt",
                "readyAt",
                "expiresAt",
                "sizeInBytes",
                "errorCode"
            ],
            json
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            details.Id,
            json
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            details.CreatedAt,
            json
                .GetProperty("createdAt")
                .GetDateTime());
        Assert.Equal(
            JsonValueKind.Null,
            json
                .GetProperty("errorCode")
                .ValueKind);
        Assert.True(receivedToken.CanBeCanceled);
        _serviceMock.Verify(
            service => service.RequestAsync(
                _memberId,
                receivedToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, null, null)]
    [InlineData(false, PersonalDataExportFailure.GenerationFailed, ErrorCodes.MemberDataExportGenerationFailed)]
    [InlineData(false, PersonalDataExportFailure.TooLarge, ErrorCodes.MemberDataExportTooLarge)]
    public async Task GetAsync_WhenRequestIsRetained_ReturnsItsStateAndStableFailureCode(
        bool latest,
        PersonalDataExportFailure? failure,
        string? code)
    {
        // Arrange
        var details = CreateDetails(
            failure.HasValue ? PersonalDataExportStatus.Failed : PersonalDataExportStatus.Queued,
            failure);
        var exportId = latest ? (Guid?)null : details.Id;
        _serviceMock
            .Setup(service => service.GetAsync(
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);
        await using var factory = new PersonalDataExportApiFactory(_serviceMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            $"{Route}/{(latest ? "latest" : details.Id.ToString("D"))}",
            TestContext.Current.CancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            code,
            json
                .GetProperty("errorCode")
                .GetString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        _serviceMock.Verify(
            service => service.GetAsync(
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> Failures()
    {
        yield return [
            new PersonalDataExportNotFoundException(),
            HttpStatusCode.NotFound,
            ErrorCodes.MemberDataExportNotFound
        ];
        yield return [
            new PersonalDataExportNotReadyException(),
            HttpStatusCode.Conflict,
            ErrorCodes.MemberDataExportNotReady
        ];
        yield return [
            new PersonalDataExportRateLimitException(),
            HttpStatusCode.TooManyRequests,
            ErrorCodes.MemberDataExportRateLimited
        ];
        yield return [
            new PersonalDataExportStorageUnavailableException(),
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.TechnicalDependencyUnavailable
        ];
        yield return [
            new InvalidAuthenticationSessionException(),
            HttpStatusCode.Unauthorized,
            ErrorCodes.AccountAuthenticationSessionInvalid
        ];
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task DownloadAsync_WhenServiceRejects_ReturnsStructuredErrorWithoutChangingCookies(
        Exception failure,
        HttpStatusCode status,
        string code)
    {
        // Arrange
        var exportId = Guid.CreateVersion7();
        _serviceMock
            .Setup(service => service.OpenArchiveAsync(
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        await using var factory = new PersonalDataExportApiFactory(_serviceMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            status,
            response.StatusCode);
        Assert.Equal(
            code,
            error?.ErrorCode);
        Assert.Equal(
            (int)status,
            error?.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "member@example.test",
                StringComparison.Ordinal));
        _serviceMock.Verify(
            service => service.OpenArchiveAsync(
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/archive")]
    public async Task GetAsync_WhenExportIdentifierIsEmpty_RejectsBeforeCallingService(string suffix)
    {
        // Arrange
        await using var factory = new PersonalDataExportApiFactory(_serviceMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            $"{Route}/{Guid.Empty:D}{suffix}",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error?.ErrorCode);
        Assert.Contains(
            error?.ValidationErrors ?? [],
            validation => validation.PropertyName == "exportId");
        Assert.False(response.Headers.Contains("Set-Cookie"));
        _serviceMock.VerifyNoOtherCalls();
    }

    private HttpClient CreateClient(PersonalDataExportApiFactory factory)
    {
        var client = factory.CreateClient();
        var token = factory.Services
            .GetRequiredService<IAccessTokenService>()
            .Create(_memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.Value);

        return client;
    }

    private static PersonalDataExportDetails CreateDetails(
        PersonalDataExportStatus status,
        PersonalDataExportFailure? failure)
    {

        return new PersonalDataExportDetails
        {
            Id = Guid.CreateVersion7(),
            Status = status,
            CreatedAt = new DateTime(
                2026,
                9,
                7,
                12,
                0,
                0,
                DateTimeKind.Utc),
            Failure = failure
        };
    }
}
