using JennGllg.Fr.MonKado.Back.Api.Abstractions;

using Microsoft.AspNetCore.Http.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>Builds public profile-photo URLs without expiring grants.</summary>
/// <param name="httpContextAccessor">The current request accessor.</param>
public class ProfileImageUrlService(IHttpContextAccessor httpContextAccessor) : IProfileImageUrlService
{
    /// <inheritdoc/>
    public string? CreateUrl(
        Guid memberId,
        Guid? imageId)
    {

        if (imageId is null)
            return null;
        var request = httpContextAccessor.HttpContext?.Request ?? throw new InvalidOperationException("An HTTP request is required to build profile image URLs.");

        return UriHelper.BuildAbsolute(
            request.Scheme,
            request.Host,
            request.PathBase,
            $"/api/v1/members/{memberId:D}/profile/image",
            new QueryString($"?imageId={imageId:D}"));
    }
}
