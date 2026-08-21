using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class AuthenticationSessionConfiguration : IEntityTypeConfiguration<AuthenticationSession>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<AuthenticationSession> builder)
    {
        builder.ToTable(
            "authentication_sessions",
            table =>
        {
            table.HasCheckConstraint(
                "ck_authentication_sessions_timestamps_consistent",
                "renewed_at >= created_at AND expires_at > created_at AND expires_at >= renewed_at");
            table.HasCheckConstraint(
                "ck_authentication_sessions_ticket_not_empty",
                "octet_length(protected_ticket) > 0");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.ProtectedTicket).IsRequired();

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
