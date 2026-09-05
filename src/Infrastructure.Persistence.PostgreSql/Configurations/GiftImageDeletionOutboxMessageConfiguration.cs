using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures durable obsolete gift-image deletions.
/// </summary>
public class GiftImageDeletionOutboxMessageConfiguration
    : IEntityTypeConfiguration<GiftImageDeletionOutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GiftImageDeletionOutboxMessage> builder)
    {
        builder.ToTable(
            "gift_image_deletion_outbox",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_gift_image_deletion_outbox_attempt_count_non_negative",
                    "attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_gift_image_deletion_outbox_timestamps_consistent",
                    "available_at >= created_at");
            });
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => message.ImageId)
            .HasDatabaseName("ux_gift_image_deletion_outbox_image_id")
            .IsUnique();
        builder.HasIndex(message => new
        {
            message.AvailableAt,
            message.CreatedAt
        })
            .HasDatabaseName("ix_gift_image_deletion_outbox_available");
    }
}
