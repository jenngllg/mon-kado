using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class MemberEmailChangeRequestConfiguration
    : IEntityTypeConfiguration<MemberEmailChangeRequest>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MemberEmailChangeRequest> builder)
    {
        builder.ToTable(
            "member_email_change_requests",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_member_email_change_requests_emails_different",
                    "current_email <> new_email");
                table.HasCheckConstraint(
                    "ck_member_email_change_requests_timestamps_consistent",
                    "expires_at > created_at AND " +
                    "(confirmed_at IS NULL OR confirmed_at >= created_at) AND " +
                    "(revoked_at IS NULL OR revoked_at >= created_at) AND " +
                    "NOT (confirmed_at IS NOT NULL AND revoked_at IS NOT NULL)");
            });

        builder.HasKey(request => request.Id);
        builder.Property(request => request.CurrentEmail)
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(request => request.NewEmail)
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(request => request.NormalizedNewEmail)
            .HasMaxLength(254)
            .IsRequired();

        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(request => request.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_member_email_change_requests_users_user_id");

        builder.HasIndex(request => request.UserId)
            .HasDatabaseName("ux_member_email_change_requests_active_user")
            .IsUnique()
            .HasFilter("confirmed_at IS NULL AND revoked_at IS NULL");
        builder.HasIndex(request => request.ExpiresAt)
            .HasDatabaseName("ix_member_email_change_requests_expires_at");
    }
}
