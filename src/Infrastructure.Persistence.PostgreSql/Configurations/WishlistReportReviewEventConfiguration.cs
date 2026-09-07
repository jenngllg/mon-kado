using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps durable private report review history and its lifecycle.</summary>
public class WishlistReportReviewEventConfiguration : IEntityTypeConfiguration<WishlistReportReviewEvent>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<WishlistReportReviewEvent> builder)
    {
        builder.ToTable(
            "wishlist_report_review_events",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_wishlist_report_review_events_status",
                    "status IN ('Pending', 'Upheld', 'Dismissed') AND previous_status IN ('Pending', 'Upheld', 'Dismissed')");
                table.HasCheckConstraint(
                    "ck_wishlist_report_review_events_sequence",
                    "sequence > 0");
            });
        builder.HasKey(review => review.Id);
        builder
            .Property(review => review.Status)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder
            .Property(review => review.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder
            .Property(review => review.Note)
            .HasMaxLength(WishlistReportTextValidation.MaximumDetailsLength);
        builder
            .HasOne<WishlistReport>()
            .WithMany()
            .HasForeignKey(review => review.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(review => review.AdministratorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasIndex(review => new
            {
                review.ReportId,
                review.Sequence
            })
            .IsUnique();
    }
}
