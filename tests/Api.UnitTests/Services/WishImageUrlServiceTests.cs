using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class WishImageUrlServiceTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        9,
        5,
        12,
        0,
        0,
        TimeSpan.Zero);

    private readonly EphemeralDataProtectionProvider _dataProtectionProvider = new();

    [Fact]
    public void CreateOwnedUrl_WhenRequestIsActive_ReturnsAbsoluteFiveMinuteGrant()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        var service = CreateService(_now);

        // Act
        var result = service.CreateOwnedUrl(
            ownerId,
            wishlistId,
            wishId,
            imageId);

        // Assert
        var uri = new Uri(result);
        Assert.Equal(
            "https://api.monkado.test/base/api/v1/wishlists/" +
            $"{wishlistId:D}/wishes/{wishId:D}/image",
            uri.GetLeftPart(UriPartial.Path));
        var token = QueryHelpers.ParseQuery(uri.Query)["token"].Single();
        var grant = service.ValidateOwned(
            token,
            wishlistId,
            wishId);
        Assert.Equal(
            ownerId,
            grant.OwnerId);
        Assert.Null(grant.ShareLinkId);
        Assert.Equal(
            imageId,
            grant.ImageId);
        Assert.Equal(
            _now.UtcDateTime.AddMinutes(5),
            grant.ExpiresAt);
    }

    [Fact]
    public void CreateSharedUrl_WhenRequestIsActive_ReturnsAbsoluteScopedGrant()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        var service = CreateService(_now);

        // Act
        var result = service.CreateSharedUrl(
            shareLinkId,
            wishlistId,
            wishId,
            imageId);

        // Assert
        var uri = new Uri(result);
        Assert.Equal(
            "https://api.monkado.test/base/api/v1/shared-wishlists/" +
            $"{shareLinkId:D}/wishes/{wishId:D}/image",
            uri.GetLeftPart(UriPartial.Path));
        var token = QueryHelpers.ParseQuery(uri.Query)["token"].Single();
        var grant = service.ValidateShared(
            token,
            shareLinkId,
            wishId);
        Assert.Equal(
            shareLinkId,
            grant.ShareLinkId);
        Assert.Null(grant.OwnerId);
        Assert.Equal(
            wishlistId,
            grant.WishlistId);
        Assert.Equal(
            imageId,
            grant.ImageId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("wrongWishlist")]
    [InlineData("wrongWish")]
    [InlineData("wrongScope")]
    public void ValidateOwned_WhenGrantIsInvalid_ThrowsNotFound(string scenario)
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var service = CreateService(_now);
        var token = CreateOwnedToken(
            service,
            wishlistId,
            wishId);

        if (scenario == "missing")
            token = null;

        if (scenario == "tampered")
            token += "altered";

        if (scenario == "wrongWishlist")
            wishlistId = Guid.CreateVersion7();

        if (scenario == "wrongWish")
            wishId = Guid.CreateVersion7();

        if (scenario == "expired")
            service = CreateService(_now.AddMinutes(5));

        // Act
        var exception = Record.Exception(() => scenario == "wrongScope"
            ? service.ValidateShared(
                token,
                Guid.CreateVersion7(),
                wishId)
            : service.ValidateOwned(
                token,
                wishlistId,
                wishId));

        // Assert
        Assert.IsType<GiftImageNotFoundException>(exception);
    }

    [Theory]
    [MemberData(nameof(InvalidGrantData))]
    public void ValidateOwned_WhenProtectedPayloadIsInvalid_ThrowsNotFound(WishImageGrant? grant)
    {
        // Arrange
        var service = CreateService(_now);
        var token = Protect(grant);

        // Act
        var exception = Record.Exception(() => service.ValidateOwned(
            token,
            grant?.WishlistId ?? Guid.CreateVersion7(),
            grant?.WishId ?? Guid.CreateVersion7()));

        // Assert
        Assert.IsType<GiftImageNotFoundException>(exception);
    }

    [Fact]
    public void ValidateOwned_WhenProtectedPayloadIsNull_ThrowsNotFound()
    {
        // Arrange
        var service = CreateService(_now);
        var token = Protect(null);

        // Act
        var exception = Record.Exception(() => service.ValidateOwned(
            token,
            Guid.CreateVersion7(),
            Guid.CreateVersion7()));

        // Assert
        Assert.IsType<GiftImageNotFoundException>(exception);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("owner")]
    [InlineData("shareLink")]
    [InlineData("wishlist")]
    [InlineData("wish")]
    public void ValidateOwned_WhenGrantShapeIsInvalid_ThrowsNotFound(string scenario)
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var grant = CreateGrant(
            scenario == "scope"
                ? "shared"
                : "owned",
            scenario == "owner"
                ? null
                : Guid.CreateVersion7(),
            scenario == "shareLink"
                ? Guid.CreateVersion7()
                : null,
            scenario == "wishlist"
                ? Guid.CreateVersion7()
                : wishlistId,
            scenario == "wish"
                ? Guid.CreateVersion7()
                : wishId);
        var service = CreateService(_now);
        var token = Protect(grant);

        // Act
        var exception = Record.Exception(() => service.ValidateOwned(
            token,
            wishlistId,
            wishId));

        // Assert
        Assert.IsType<GiftImageNotFoundException>(exception);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("owner")]
    [InlineData("shareLink")]
    [InlineData("shareLinkNull")]
    [InlineData("wish")]
    public void ValidateShared_WhenGrantShapeIsInvalid_ThrowsNotFound(string scenario)
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var grantShareLinkId = (Guid?)shareLinkId;

        if (scenario == "shareLink")
            grantShareLinkId = Guid.CreateVersion7();

        if (scenario == "shareLinkNull")
            grantShareLinkId = null;
        var grant = CreateGrant(
            scenario == "scope"
                ? "owned"
                : "shared",
            scenario == "owner"
                ? Guid.CreateVersion7()
                : null,
            grantShareLinkId,
            Guid.CreateVersion7(),
            scenario == "wish"
                ? Guid.CreateVersion7()
                : wishId);
        var service = CreateService(_now);
        var token = Protect(grant);

        // Act
        var exception = Record.Exception(() => service.ValidateShared(
            token,
            shareLinkId,
            wishId));

        // Assert
        Assert.IsType<GiftImageNotFoundException>(exception);
    }

    [Fact]
    public void ValidateOwned_WhenProtectedPayloadIsInvalidJson_ThrowsNotFound()
    {
        // Arrange
        var service = CreateService(_now);
        var protector = _dataProtectionProvider.CreateProtector("MonKado.WishImages.Url.v1");
        var token = protector.Protect("{");

        // Act
        var exception = Record.Exception(() => service.ValidateOwned(
            token,
            Guid.CreateVersion7(),
            Guid.CreateVersion7()));

        // Assert
        Assert.IsType<GiftImageNotFoundException>(exception);
    }

    [Fact]
    public void CreateOwnedUrl_WhenHttpContextIsMissing_ThrowsInvalidOperation()
    {
        // Arrange
        var service = new WishImageUrlService(
            _dataProtectionProvider,
            new HttpContextAccessor(),
            new FixedTimeProvider(_now));

        // Act
        var exception = Record.Exception(() => service.CreateOwnedUrl(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7()));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    public static TheoryData<WishImageGrant?> InvalidGrantData()
    {
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();

        return
        [
            new WishImageGrant
            {
                Version = 2,
                Scope = "owned",
                OwnerId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                WishId = wishId,
                ImageId = imageId,
                ExpiresAt = _now.UtcDateTime.AddMinutes(1)
            },
            new WishImageGrant
            {
                Version = 1,
                Scope = "owned",
                OwnerId = Guid.CreateVersion7(),
                WishlistId = Guid.Empty,
                WishId = wishId,
                ImageId = imageId,
                ExpiresAt = _now.UtcDateTime.AddMinutes(1)
            },
            new WishImageGrant
            {
                Version = 1,
                Scope = "owned",
                OwnerId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                WishId = Guid.Empty,
                ImageId = imageId,
                ExpiresAt = _now.UtcDateTime.AddMinutes(1)
            },
            new WishImageGrant
            {
                Version = 1,
                Scope = "owned",
                OwnerId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                WishId = wishId,
                ImageId = Guid.Empty,
                ExpiresAt = _now.UtcDateTime.AddMinutes(1)
            },
            new WishImageGrant
            {
                Version = 1,
                Scope = "owned",
                OwnerId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                WishId = wishId,
                ImageId = imageId,
                ExpiresAt = DateTime.SpecifyKind(
                    _now.UtcDateTime.AddMinutes(1),
                    DateTimeKind.Unspecified)
            }
        ];
    }

    private WishImageUrlService CreateService(DateTimeOffset now)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.monkado.test");
        context.Request.PathBase = "/base";
        var accessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        return new WishImageUrlService(
            _dataProtectionProvider,
            accessor,
            new FixedTimeProvider(now));
    }

    private static string CreateOwnedToken(
        WishImageUrlService service,
        Guid wishlistId,
        Guid wishId)
    {
        var url = service.CreateOwnedUrl(
            Guid.CreateVersion7(),
            wishlistId,
            wishId,
            Guid.CreateVersion7());

        return QueryHelpers.ParseQuery(new Uri(url).Query)["token"].ToString();
    }

    private string Protect(WishImageGrant? grant)
    {
        var protector = _dataProtectionProvider.CreateProtector("MonKado.WishImages.Url.v1");

        return protector.Protect(JsonSerializer.Serialize(grant));
    }

    private static WishImageGrant CreateGrant(
        string scope,
        Guid? ownerId,
        Guid? shareLinkId,
        Guid wishlistId,
        Guid wishId)
    {
        return new WishImageGrant
        {
            Version = 1,
            Scope = scope,
            OwnerId = ownerId,
            ShareLinkId = shareLinkId,
            WishlistId = wishlistId,
            WishId = wishId,
            ImageId = Guid.CreateVersion7(),
            ExpiresAt = _now.UtcDateTime.AddMinutes(1)
        };
    }
}
