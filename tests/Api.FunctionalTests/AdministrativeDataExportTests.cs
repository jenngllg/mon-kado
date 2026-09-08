using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class AdministrativeDataExportTests
{
    private readonly Mock<IAdministrativeDataExportService> _serviceMock = new(MockBehavior.Strict);
    private readonly Mock<IAdministratorAccessService> _accessMock = new(MockBehavior.Strict);
    private readonly Guid _administratorId = Guid.CreateVersion7();
    private readonly Guid _memberId = Guid.CreateVersion7();
    public AdministrativeDataExportTests()
    {
        _accessMock
            .Setup(service => service.GetAccessAsync(
                _administratorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdministratorAccess.Granted);
    }

    [Theory]
    [InlineData(PersonalDataExportStatus.Queued, HttpStatusCode.Accepted)]
    [InlineData(PersonalDataExportStatus.Processing, HttpStatusCode.Accepted)]
    [InlineData(PersonalDataExportStatus.Ready, HttpStatusCode.OK)]
    public async Task RequestAsync_WhenReferenceIsValid_ReturnsSharedMetadataAndAdministrativeLocation(
        PersonalDataExportStatus status,
        HttpStatusCode expectedStatus)
    {
        // Arrange
        var details = TestFixture
            .Create()
            .Build<PersonalDataExportDetails>()
            .With(
            export => export.Status,
            status)
            .Without(export => export.Failure)
            .Create();
        _serviceMock
            .Setup(service => service.RequestAsync(
                _administratorId,
                _memberId,
                "SUPPORT-807",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);
        await using var factory = new AdministrativeDataExportApiFactory(
            _serviceMock.Object,
            _accessMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(),
            new
            {
                requestReference = "  SUPPORT-807  "
            },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        Assert.Equal(
            $"{GetRoute()}/{details.Id:D}",
            response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            [
                "createdAt",
                "errorCode",
                "expiresAt",
                "id",
                "readyAt",
                "sizeInBytes",
                "snapshotAt",
                "status"
            ],
            body
                .EnumerateObject()
                .Select(property => property.Name)
                .Order());
        Assert.Equal(
            details.Id,
            body
                .GetProperty("id")
                .GetGuid());
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "SUPPORT-807",
                StringComparison.Ordinal));
        _serviceMock.Verify(
            service => service.RequestAsync(
                _administratorId,
                _memberId,
                "SUPPORT-807",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SUPPORT\n807")]
    [InlineData("\tSUPPORT-807")]
    public async Task RequestAsync_WhenReferenceIsInvalid_RejectsBeforeServiceInvocation(string? reference)
    {
        // Arrange
        await using var factory = new AdministrativeDataExportApiFactory(
            _serviceMock.Object,
            _accessMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(),
            new
            {
                requestReference = reference
            },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Contains(
            body
                .GetProperty("validationErrors")
                .EnumerateArray(),
            error => error
                .GetProperty("propertyName")
                .GetString() == "requestReference");
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_WhenAdministrativelyRequested_UsesTargetAndOptionalExportIdentifier(bool latest)
    {
        // Arrange
        var details = TestFixture
            .Create()
            .Create<PersonalDataExportDetails>();
        var exportId = latest ? (Guid?)null : details.Id;
        _serviceMock
            .Setup(service => service.GetAsync(
                _administratorId,
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);
        await using var factory = new AdministrativeDataExportApiFactory(
            _serviceMock.Object,
            _accessMock.Object);
        using var client = CreateClient(factory);
        var suffix = latest ? "latest" : details.Id.ToString("D");

        // Act
        using var response = await client.GetAsync(
            $"{GetRoute()}/{suffix}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        _serviceMock.Verify(
            service => service.GetAsync(
                _administratorId,
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    [Fact]
    public async Task DownloadAsync_WhenAudited_ReturnsPrivateAttachment()
    {
        // Arrange
        var exportId = Guid.CreateVersion7();
        var bytes = new byte[]
        {
            80,
            75,
            3,
            4
        };
        _serviceMock
            .Setup(service => service.OpenArchiveAsync(
                _administratorId,
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonalDataExportDownload
            {
                ExportId = exportId,
                Content = new MemoryStream(bytes)
            });
        await using var factory = new AdministrativeDataExportApiFactory(
            _serviceMock.Object,
            _accessMock.Object);
        using var client = CreateClient(factory);

        // Act
        using var response = await client.GetAsync(
            $"{GetRoute()}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "application/zip",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "attachment",
            response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(
            "nosniff",
            Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            bytes,
            body);
        _serviceMock.Verify(
            service => service.OpenArchiveAsync(
                _administratorId,
                _memberId,
                exportId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
        VerifyAuthorization();
    }

    private HttpClient CreateClient(AdministrativeDataExportApiFactory factory)
    {
        var client = factory.CreateClient();
        var tokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokenService
                .Create(_administratorId)
                .Value);

        return client;
    }

    private string GetRoute()
    {

        return $"/api/v1/admin/members/{_memberId:D}/data-exports";
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
