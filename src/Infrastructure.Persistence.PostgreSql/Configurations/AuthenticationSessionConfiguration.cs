using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class AuthenticationSessionConfiguration : IEntityTypeConfiguration<AuthenticationSession>
{
    public void Configure(EntityTypeBuilder<AuthenticationSession> builder)
    {
        builder.ToTable(
            "authentication_sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_authentication_sessions_refresh_token_hash_length",
                    "octet_length(refresh_token_hash) = 32");
                table.HasCheckConstraint(
                    "ck_authentication_sessions_timestamps_consistent",
                    "renewed_at >= created_at AND expires_at > created_at AND expires_at >= renewed_at " +
                    "AND (revoked_at IS NULL OR revoked_at >= created_at)");
            });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.RefreshTokenHash).IsRequired();

        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_authentication_sessions_users_user_id");

        builder.HasIndex(session => session.UserId)
            .HasDatabaseName("ix_authentication_sessions_user_id");
        builder.HasIndex(session => session.ExpiresAt)
            .HasDatabaseName("ix_authentication_sessions_expires_at");
    }
}
