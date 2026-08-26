using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures guest sessions for PostgreSQL.
/// </summary>
public class GuestSessionConfiguration : IEntityTypeConfiguration<GuestSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GuestSession> builder)
    {
        builder.ToTable(
            "guest_sessions",
            table => table.HasCheckConstraint(
                "ck_guest_sessions_secret_hash_length",
                "octet_length(secret_hash) = 32"));
        builder.HasKey(session => session.Id);
        builder.Property(session => session.SecretHash)
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(session => session.ExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(session => session.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(session => session.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(session => session.ExpiresAt)
            .HasDatabaseName("ix_guest_sessions_expires_at");
    }
}
