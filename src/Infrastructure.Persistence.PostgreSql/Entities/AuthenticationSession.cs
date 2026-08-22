namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Represents a renewable authentication session.
/// </summary>
public class AuthenticationSession
{
    /// <summary>
    /// Gets or sets the session identifier.
    /// </summary>
    public Guid Id
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the member identifier.
    /// </summary>
    public Guid UserId
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the refresh token hash.
    /// </summary>
    public byte[] RefreshTokenHash { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the browser cookie is persistent.
    /// </summary>
    public bool IsPersistent
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the creation date and time.
    /// </summary>
    public DateTime CreatedAt
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the last renewal date and time.
    /// </summary>
    public DateTime RenewedAt
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the expiration date and time.
    /// </summary>
    public DateTime ExpiresAt
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the revocation date and time.
    /// </summary>
    public DateTime? RevokedAt
    {
        get; set;
    }

    /// <summary>
    /// Creates an authentication session.
    /// </summary>
    /// <param name="id">The session identifier.</param>
    /// <param name="userId">The member identifier.</param>
    /// <param name="refreshTokenHash">The refresh token hash.</param>
    /// <param name="isPersistent">Whether the browser cookie is persistent.</param>
    /// <param name="now">The creation date and time.</param>
    /// <param name="expiresAt">The expiration date and time.</param>
    /// <returns>The authentication session.</returns>
    public static AuthenticationSession Create(
        Guid id,
        Guid userId,
        byte[] refreshTokenHash,
        bool isPersistent,
        DateTime now,
        DateTime expiresAt)
    {
        return new AuthenticationSession
        {
            Id = id,
            UserId = userId,
            RefreshTokenHash = refreshTokenHash,
            IsPersistent = isPersistent,
            CreatedAt = now,
            RenewedAt = now,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Rotates the session refresh token.
    /// </summary>
    /// <param name="refreshTokenHash">The new refresh token hash.</param>
    /// <param name="renewedAt">The renewal date and time.</param>
    /// <param name="expiresAt">The expiration date and time.</param>
    public void Rotate(
        byte[] refreshTokenHash,
        DateTime renewedAt,
        DateTime expiresAt)
    {
        RefreshTokenHash = refreshTokenHash;
        RenewedAt = renewedAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Revokes the authentication session.
    /// </summary>
    /// <param name="revokedAt">The revocation date and time.</param>
    public void Revoke(DateTime revokedAt)
    {
        RevokedAt ??= revokedAt;
    }
}
