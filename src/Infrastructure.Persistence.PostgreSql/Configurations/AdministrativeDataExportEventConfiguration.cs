using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps retained audit events without coupling them to short-lived export rows.</summary>
public class AdministrativeDataExportEventConfiguration : IEntityTypeConfiguration<AdministrativeDataExportEvent>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AdministrativeDataExportEvent> builder)
    {
        builder.ToTable(
            "administrative_data_export_events",
            table => table.HasCheckConstraint(
                "ck_administrative_data_export_events_action",
                "action IN ('Requested', 'DownloadStarted')"));
        builder.HasKey(audit => audit.Id);
        builder
            .Property(audit => audit.Action)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder
            .Property(audit => audit.RequestReference)
            .HasMaxLength(128);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(audit => audit.AdministratorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(audit => audit.MemberId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasIndex(audit => new
            {
                audit.MemberId,
                audit.ExportId,
                audit.CreatedAt,
                audit.Id
            })
            .IsDescending(
            false,
            false,
            true,
            true);
        builder.HasIndex(audit => audit.CreatedAt);
        builder.HasIndex(audit => new
        {
            audit.CreatedAt,
            audit.Id
        });
    }
}
