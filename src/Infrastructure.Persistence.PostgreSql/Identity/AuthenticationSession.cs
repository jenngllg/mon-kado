using Microsoft.AspNetCore.Authentication;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

public sealed class AuthenticationSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] ProtectedTicket { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset RenewedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    internal static AuthenticationSession Create(
        Guid id,
        Guid userId,
        AuthenticationTicket ticket,
        byte[] protectedTicket,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserId = userId,
            ProtectedTicket = protectedTicket,
            CreatedAt = now,
            RenewedAt = now,
            ExpiresAt = ticket.Properties.ExpiresUtc
                ?? throw new InvalidOperationException("Authentication tickets require an expiration.")
        };
}
