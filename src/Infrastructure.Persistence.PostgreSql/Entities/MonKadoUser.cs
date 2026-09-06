using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
/// <summary>
/// Represents mon kado user.
/// </summary>

public class MonKadoUser : IdentityUser<Guid>, IAuditableEntity
{
    /// <summary>
    /// Gets display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets the current normalized profile-photo identifier.</summary>
    public Guid? ProfileImageId
    {
        get; private set;
    }

    /// <summary>Gets the SHA-256 hash of the normalized profile photo.</summary>
    public byte[]? ProfileImageHash
    {
        get; private set;
    }

    /// <summary>Replaces the profile-photo reference and its normalized content hash.</summary>
    /// <param name="imageId">The new image identifier.</param>
    /// <param name="contentHash">The normalized content hash.</param>
    public void SetProfileImage(
        Guid imageId,
        byte[] contentHash)
    {
        ProfileImageId = imageId;
        ProfileImageHash = contentHash.ToArray();
    }

    /// <summary>Removes the profile-photo reference and content hash together.</summary>
    public void RemoveProfileImage()
    {
        ProfileImageId = null;
        ProfileImageHash = null;
    }
    /// <summary>
    /// Gets created at.
    /// </summary>

    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets updated at.
    /// </summary>

    public DateTime? UpdatedAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets unconfirmed account expires at.
    /// </summary>

    public DateTime? UnconfirmedAccountExpiresAt
    {
        get; set;
    }
    /// <summary>
    /// Gets version.
    /// </summary>

    public uint Version
    {
        get; private set;
    }
}
