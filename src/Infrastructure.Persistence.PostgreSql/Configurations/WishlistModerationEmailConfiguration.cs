using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps leased, ordered moderation notifications.</summary>
public class WishlistModerationEmailConfiguration : IEntityTypeConfiguration<WishlistModerationEmail>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<WishlistModerationEmail> builder)
    {
        builder.ToTable(
            "wishlist_moderation_email_outbox",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_wishlist_moderation_email_attempts",
                    "attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_wishlist_moderation_email_lease",
                    "(lease_id IS NULL) = (locked_until IS NULL)");
                table.HasCheckConstraint(
                    "ck_wishlist_moderation_email_dates",
                    "available_at >= created_at AND (processed_at IS NULL OR processed_at >= created_at)");
            });
        builder.HasKey(value => value.Id);
        builder
            .Property(value => value.Id)
            .ValueGeneratedNever();
        builder
            .Property(value => value.LastError)
            .HasMaxLength(64);
        builder
            .HasOne<WishlistModerationEvent>()
            .WithOne()
            .HasForeignKey<WishlistModerationEmail>(value => value.Id)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasIndex(value => new { value.AvailableAt, value.Id })
            .HasFilter("processed_at IS NULL");
        builder
            .HasIndex(value => new { value.ProcessedAt, value.Id })
            .HasFilter("processed_at IS NOT NULL");
    }
}
