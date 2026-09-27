using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class PublicMemberProfileTests
{
    private static readonly string[] _profileProperties =
    [
        "id",
        "displayName",
        "profileImageUrl",
        "wishlists"
    ];
    private static readonly string[] _listProperties =
    [
        "id",
        "name",
        "occasion",
        "eventDate",
        "shareUrl"
    ];
    private readonly Mock<IPublicMemberProfileService> _serviceMock;

    public PublicMemberProfileTests()
    {
        _serviceMock = new Mock<IPublicMemberProfileService>(MockBehavior.Strict);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_WhenAnonymous_ReturnsOnlyPublicContractOrNotFound(bool found)
    {
        // Arrange
        var profile = PublicMemberProfileTestData.CreateWithSharedList();
        var id = profile.Id;
        var imageId = Assert.IsType<Guid>(profile.ProfileImageId);
        var shareId = profile.Wishlists[0].ShareLinkId;
        var secret = profile.Wishlists[0].Secret;
        _serviceMock
            .Setup(service => service.GetAsync(
                id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(found ? profile : null);
        await using var baseline = new RegistrationApiFactory();
        await using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPublicMemberProfileService>();
            services.AddSingleton(_serviceMock.Object);
        }));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/members/{id}/profile",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            found ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        if (found)
        {
            Assert.Equal(
                _profileProperties,
                body.EnumerateObject()
                    .Select(property => property.Name));
            Assert.Equal(
                id,
                body.GetProperty("id")
                    .GetGuid());
            Assert.Contains(
                imageId.ToString(),
                body.GetProperty("profileImageUrl")
                    .GetString());
            var list = Assert.Single(body.GetProperty("wishlists")
                .EnumerateArray());
            Assert.Equal(
                _listProperties,
                list.EnumerateObject()
                    .Select(property => property.Name));
            Assert.Equal(
                $"http://localhost:5173/shared-wishlists/{shareId}#{secret}",
                list.GetProperty("shareUrl")
                    .GetString());
        }
        else
        {
            Assert.Equal(
                "ACCOUNT_PUBLIC_PROFILE_NOT_FOUND",
                body.GetProperty("errorCode")
                    .GetString());
        }

        Assert.DoesNotContain(
            baseline.LogMessages,
            message => message.Contains(profile.DisplayName) || message.Contains(secret));
        _serviceMock.Verify(
            service => service.GetAsync(
                id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenIdentifierIsEmpty_RejectsBeforePersistence()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/members/{Guid.Empty}/profile",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WhenDatabaseUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/members/{Guid.CreateVersion7()}/profile",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WhenQuotaExceeded_ReturnsRateLimit()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            using var invalid = await client.GetAsync(
                $"/api/v1/members/{Guid.Empty}/profile",
                TestContext.Current.CancellationToken);
        }

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/members/{Guid.Empty}/profile",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task OpenApiAsync_WhenProfileDocumented_ExposesAnonymousMinimalSchema()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        var responses = document.GetProperty("paths")
            .GetProperty("/api/v1/members/{memberId}/profile")
            .GetProperty("get")
            .GetProperty("responses");
        Assert.False(responses.TryGetProperty(
            "401",
            out _));
        Assert.True(responses.TryGetProperty(
            "404",
            out _));
        var properties = document.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("PublicMemberProfileResponse")
            .GetProperty("properties");
        Assert.Equal(
            _profileProperties,
            properties.EnumerateObject()
                .Select(property => property.Name));
    }
}
