using Microsoft.AspNetCore.Authentication;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
/// <summary>
/// Represents authentication session.
/// </summary>

public class AuthenticationSession
{
    /// <summary>
    /// Gets id.
    /// </summary>
    public Guid Id
    {
        get; set;
    }
    /// <summary>
    /// Gets user id.
    /// </summary>

    public Guid UserId
    {
        get; set;
    }
    /// <summary>
    /// Gets protected ticket.
    /// </summary>

    public byte[] ProtectedTicket { get; set; } = [];
    /// <summary>
    /// Gets created at.
    /// </summary>

    public DateTime CreatedAt
    {
        get; set;
    }
    /// <summary>
    /// Gets renewed at.
    /// </summary>

    public DateTime RenewedAt
    {
        get; set;
    }
    /// <summary>
    /// Gets expires at.
    /// </summary>

    public DateTime ExpiresAt
    {
        get; set;
    }

    internal static AuthenticationSession Create(
        Guid id,
        Guid userId,
        AuthenticationTicket ticket,
        byte[] protectedTicket,
        DateTime now)
    {

        return new()
        {
            Id = id,
            UserId = userId,
            ProtectedTicket = protectedTicket,
            CreatedAt = now,
            RenewedAt = now,
            ExpiresAt = ticket.Properties.ExpiresUtc?.UtcDateTime
                ?? throw new InvalidOperationException("Authentication tickets require an expiration.")
        };
    }
}
