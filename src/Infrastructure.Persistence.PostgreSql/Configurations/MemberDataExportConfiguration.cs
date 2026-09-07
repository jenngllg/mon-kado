using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps durable exports and enforces member-scoped active-request uniqueness.</summary>
public class MemberDataExportConfiguration : IEntityTypeConfiguration<MemberDataExport>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<MemberDataExport> builder)
    {
        builder.ToTable(
            "member_data_exports",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_member_data_exports_status",
                    "status IN ('Queued', 'Processing', 'Ready', 'Failed', 'Expired')");
                table.HasCheckConstraint(
                    "ck_member_data_exports_attempts",
                    "attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_member_data_exports_lease",
                    "(status = 'Processing' AND lease_id IS NOT NULL AND locked_until IS NOT NULL) OR " + "(status <> 'Processing' AND lease_id IS NULL AND locked_until IS NULL)");
                table.HasCheckConstraint(
                    "ck_member_data_exports_archive",
                    "(status IN ('Ready', 'Expired') AND archive_id IS NOT NULL AND snapshot_at IS NOT NULL AND ready_at IS NOT NULL AND expires_at IS NOT NULL AND size_in_bytes IS NOT NULL) OR " + "(status NOT IN ('Ready', 'Expired') AND archive_id IS NULL AND snapshot_at IS NULL AND ready_at IS NULL AND expires_at IS NULL AND size_in_bytes IS NULL)");
                table.HasCheckConstraint(
                    "ck_member_data_exports_failure",
                    "(status = 'Failed' AND failure IS NOT NULL AND failure IN ('GenerationFailed', 'TooLarge')) OR (status <> 'Failed' AND failure IS NULL)");
                table.HasCheckConstraint(
                    "ck_member_data_exports_dates",
                    "available_at >= created_at AND (expires_at IS NULL OR expires_at > ready_at) AND (size_in_bytes IS NULL OR size_in_bytes > 0)");
            });
        builder.HasKey(export => export.Id);
        builder
            .Property(export => export.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder
            .Property(export => export.Failure)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(export => export.MemberId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasIndex(export => export.MemberId)
            .IsUnique()
            .HasFilter("member_id IS NOT NULL AND status IN ('Queued', 'Processing', 'Ready')")
            .HasDatabaseName("ux_member_data_exports_active_member");
        builder
            .HasIndex(export => new
            {
                export.MemberId,
                export.CreatedAt,
                export.Id
            })
            .IsDescending(
            false,
            true,
            true);
        builder
            .HasIndex(export => new
            {
                export.AvailableAt,
                export.CreatedAt,
                export.Id
            })
            .HasFilter("status IN ('Queued', 'Processing')");
        builder
            .HasIndex(export => export.ExpiresAt)
            .HasFilter("status = 'Ready'");
    }
}
