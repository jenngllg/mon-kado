using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Protects short-lived gift-image grants and builds absolute API URLs.
/// </summary>
public class WishImageUrlService : IWishImageUrlService
{
    private const int CurrentVersion = 1;
    private const string OwnedScope = "owned";
    private const string SharedScope = "shared";
    private const string ProtectorPurpose = "MonKado.WishImages.Url.v1";

    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WishImageUrlService" /> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data-protection provider.</param>
    /// <param name="httpContextAccessor">The current HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    public WishImageUrlService(
        IDataProtectionProvider dataProtectionProvider,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string CreateOwnedUrl(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId)
    {
        var grant = CreateGrant(
            OwnedScope,
            ownerId,
            null,
            wishlistId,
            wishId,
            imageId);

        return BuildAbsoluteUrl(
            $"/api/v1/wishlists/{wishlistId:D}/wishes/{wishId:D}/image",
            grant);
    }

    /// <inheritdoc />
    public string CreateSharedUrl(
        Guid shareLinkId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId)
    {
        var grant = CreateGrant(
            SharedScope,
            null,
            shareLinkId,
            wishlistId,
            wishId,
            imageId);

        return BuildAbsoluteUrl(
            $"/api/v1/shared-wishlists/{shareLinkId:D}/wishes/{wishId:D}/image",
            grant);
    }

    /// <inheritdoc />
    public WishImageGrant ValidateOwned(
        string? token,
        Guid wishlistId,
        Guid wishId)
    {
        var grant = Unprotect(token);

        if (!string.Equals(
                grant.Scope,
                OwnedScope,
                StringComparison.Ordinal) ||
            grant.OwnerId is null ||
            grant.ShareLinkId is not null ||
            grant.WishlistId != wishlistId ||
            grant.WishId != wishId)
        {
            throw new GiftImageNotFoundException();
        }

        return grant;
    }

    /// <inheritdoc />
    public WishImageGrant ValidateShared(
        string? token,
        Guid shareLinkId,
        Guid wishId)
    {
        var grant = Unprotect(token);

        if (!string.Equals(
                grant.Scope,
                SharedScope,
                StringComparison.Ordinal) ||
            grant.OwnerId is not null ||
            grant.ShareLinkId != shareLinkId ||
            grant.WishId != wishId)
        {
            throw new GiftImageNotFoundException();
        }

        return grant;
    }

    /// <summary>
    /// Creates a short-lived grant payload for one immutable image reference.
    /// </summary>
    /// <param name="scope">The owned or shared scope.</param>
    /// <param name="ownerId">The optional owner identifier.</param>
    /// <param name="shareLinkId">The optional share-link identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="imageId">The immutable image identifier.</param>
    /// <returns>The unprotected grant payload.</returns>
    private WishImageGrant CreateGrant(
        string scope,
        Guid? ownerId,
        Guid? shareLinkId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId)
    {
        return new WishImageGrant
        {
            Version = CurrentVersion,
            Scope = scope,
            OwnerId = ownerId,
            ShareLinkId = shareLinkId,
            WishlistId = wishlistId,
            WishId = wishId,
            ImageId = imageId,
            ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.Add(_lifetime)
        };
    }

    /// <summary>
    /// Protects a grant and appends it to an absolute API image URL.
    /// </summary>
    /// <param name="path">The image route path.</param>
    /// <param name="grant">The grant to protect.</param>
    /// <returns>The absolute signed URL.</returns>
    /// <exception cref="InvalidOperationException">No active HTTP request is available.</exception>
    private string BuildAbsoluteUrl(
        string path,
        WishImageGrant grant)
    {
        var request = _httpContextAccessor.HttpContext?.Request ??
            throw new InvalidOperationException("An active HTTP request is required.");
        var origin = string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent());
        var token = _protector.Protect(JsonSerializer.Serialize(grant));

        return QueryHelpers.AddQueryString(
            origin + path,
            "token",
            token);
    }

    /// <summary>
    /// Unprotects and validates the common structure and expiration of a grant.
    /// </summary>
    /// <param name="token">The protected grant.</param>
    /// <returns>The valid unprotected grant.</returns>
    /// <exception cref="GiftImageNotFoundException">The token is missing, malformed, or expired.</exception>
    private WishImageGrant Unprotect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new GiftImageNotFoundException();

        try
        {
            var grant = JsonSerializer.Deserialize<WishImageGrant>(_protector.Unprotect(token));

            if (grant is null ||
                grant.Version != CurrentVersion ||
                grant.WishlistId == Guid.Empty ||
                grant.WishId == Guid.Empty ||
                grant.ImageId == Guid.Empty ||
                grant.ExpiresAt.Kind is not DateTimeKind.Utc ||
                grant.ExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                throw new GiftImageNotFoundException();
            }

            return grant;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new GiftImageNotFoundException();
        }
    }
}
