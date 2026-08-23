namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Represents a temporary request to replace a member email address.
/// </summary>
public class MemberEmailChangeRequest
{
    private MemberEmailChangeRequest()
    {
    }

    /// <summary>
    /// Gets the request identifier.
    /// </summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>
    /// Gets the member identifier.
    /// </summary>
    public Guid UserId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the email address active when the request was created.
    /// </summary>
    public string CurrentEmail
    {
        get; private set;
    } = string.Empty;

    /// <summary>
    /// Gets the requested email address.
    /// </summary>
    public string NewEmail
    {
        get; private set;
    } = string.Empty;

    /// <summary>
    /// Gets the normalized requested email address.
    /// </summary>
    public string NormalizedNewEmail
    {
        get; private set;
    } = string.Empty;

    /// <summary>
    /// Gets the creation date and time.
    /// </summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the expiration date and time.
    /// </summary>
    public DateTime ExpiresAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the confirmation date and time.
    /// </summary>
    public DateTime? ConfirmedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the revocation date and time.
    /// </summary>
    public DateTime? RevokedAt
    {
        get; private set;
    }

    /// <summary>
    /// Creates a member email change request.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="currentEmail">The currently active email address.</param>
    /// <param name="newEmail">The requested email address.</param>
    /// <param name="normalizedNewEmail">The normalized requested email address.</param>
    /// <param name="createdAt">The creation date and time.</param>
    /// <param name="expiresAt">The expiration date and time.</param>
    /// <returns>The created request.</returns>
    public static MemberEmailChangeRequest Create(
        Guid userId,
        string currentEmail,
        string newEmail,
        string normalizedNewEmail,
        DateTime createdAt,
        DateTime expiresAt)
    {

        return new MemberEmailChangeRequest
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAt)),
            UserId = userId,
            CurrentEmail = currentEmail,
            NewEmail = newEmail,
            NormalizedNewEmail = normalizedNewEmail,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Determines whether the request can still be confirmed.
    /// </summary>
    /// <param name="now">The current date and time.</param>
    /// <returns><see langword="true" /> when the request is active.</returns>
    public bool IsActive(DateTime now)
    {

        return ConfirmedAt is null &&
            RevokedAt is null &&
            ExpiresAt > now;
    }

    /// <summary>
    /// Revokes the request.
    /// </summary>
    /// <param name="revokedAt">The revocation date and time.</param>
    public void Revoke(DateTime revokedAt)
    {
        RevokedAt ??= revokedAt;
    }

    /// <summary>
    /// Marks the request as confirmed.
    /// </summary>
    /// <param name="confirmedAt">The confirmation date and time.</param>
    public void Confirm(DateTime confirmedAt)
    {
        ConfirmedAt = confirmedAt;
    }
}
