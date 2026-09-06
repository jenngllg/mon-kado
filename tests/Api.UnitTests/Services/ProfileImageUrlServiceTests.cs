using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class ProfileImageUrlServiceTests
{
    private readonly HttpContextAccessor _accessor = new();
    private readonly ProfileImageUrlService _service;
    public ProfileImageUrlServiceTests()
    {
        _service = new ProfileImageUrlService(_accessor);
    }

    [Fact]
    public void CreateUrl_WhenPhotoExists_ReturnsAbsoluteVersionedUrlWithPathBase()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        _accessor.HttpContext = new DefaultHttpContext();
        _accessor.HttpContext.Request.Scheme = "https";
        _accessor.HttpContext.Request.Host = new HostString("api.monkado.test");
        _accessor.HttpContext.Request.PathBase = "/base";

        // Act
        var result = _service.CreateUrl(
            memberId,
            imageId);

        // Assert
        var uri = new Uri(Assert.IsType<string>(result));
        Assert.Equal(
            $"https://api.monkado.test/base/api/v1/members/{memberId:D}/profile/image",
            uri.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Single(query);
        Assert.Equal(
            imageId.ToString("D"),
            query["imageId"]);
    }

    [Fact]
    public void CreateUrl_WhenPhotoIsAbsent_ReturnsNullWithoutAnHttpRequest()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();

        // Act
        var result = _service.CreateUrl(
            memberId,
            null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CreateUrl_WhenPhotoExistsWithoutHttpRequest_ThrowsExplicitConfigurationError()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _service.CreateUrl(
                memberId,
                imageId));

        // Assert
        Assert.Contains(
            "HTTP request",
            exception.Message,
            StringComparison.Ordinal);
    }
}
