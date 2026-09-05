using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures anonymous wishlist reports for PostgreSQL.
/// </summary>
public class WishlistReportConfiguration : IEntityTypeConfiguration<WishlistReport>
{
    private const int MaximumReasonLength = 32;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WishlistReport> builder)
    {
        builder.ToTable("wishlist_reports");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Reason)
            .HasConversion<string>()
            .HasMaxLength(MaximumReasonLength)
            .IsRequired();
        builder.Property(report => report.Details)
            .HasMaxLength(WishlistReportTextValidation.MaximumDetailsLength);
        builder.Property(report => report.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(report => report.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.HasOne<Wishlist>()
            .WithMany()
            .HasForeignKey(report => report.WishlistId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wishlist_reports_wishlists_wishlist_id");
        builder.HasIndex(report => report.WishlistId)
            .HasDatabaseName("ix_wishlist_reports_wishlist_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_wishlist_reports_reason_valid",
                "reason IN ('SpamOrScam', 'InappropriateContent', 'PrivacyViolation', 'Other')");
            table.HasCheckConstraint(
                "ck_wishlist_reports_timestamps_consistent",
                "updated_at IS NULL OR updated_at >= created_at");
        });
    }
}
