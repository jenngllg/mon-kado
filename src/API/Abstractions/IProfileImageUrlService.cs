namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>Builds versioned public URLs for profile photos.</summary>
public interface IProfileImageUrlService
{
    /// <summary>Creates an absolute public URL when the account has a photo.</summary>
    /// <param name="memberId">The public member identifier.</param>
    /// <param name="imageId">The current photo identifier, or null for a generated avatar.</param>
    /// <returns>The absolute URL, or null when no photo is stored.</returns>
    /// <exception cref="InvalidOperationException">A photo exists but no HTTP request is available.</exception>
    string? CreateUrl(
        Guid memberId,
        Guid? imageId);
}
