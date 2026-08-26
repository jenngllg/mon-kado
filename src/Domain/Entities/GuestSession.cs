using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents a persistent anonymous browser identity.
/// </summary>
public class GuestSession : IAuditableEntity
{
    private GuestSession()
    {
    }

    /// <summary>
    /// Initializes a new guest session.
    /// </summary>
    /// <param name="id">The guest session identifier.</param>
    /// <param name="secretHash">The SHA-256 hash of the browser secret.</param>
    /// <param name="expiresAt">The absolute UTC expiration.</param>
    public GuestSession(
        Guid id,
        byte[] secretHash,
        DateTime expiresAt)
    {
        Id = id;
        SecretHash = secretHash.ToArray();
        ExpiresAt = expiresAt;
    }

    /// <summary>Gets the guest session identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the SHA-256 hash of the browser secret.</summary>
    public byte[] SecretHash { get; private set; } = [];

    /// <summary>Gets the absolute UTC expiration.</summary>
    public DateTime ExpiresAt
    {
        get; private set;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter through change tracking.")]
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter through change tracking.")]
    public DateTime? UpdatedAt
    {
        get; private set;
    }
}
