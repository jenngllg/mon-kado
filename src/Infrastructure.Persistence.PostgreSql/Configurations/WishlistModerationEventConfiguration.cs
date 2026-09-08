using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps durable private administrator decisions.</summary>
public class WishlistModerationEventConfiguration : IEntityTypeConfiguration<WishlistModerationEvent>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<WishlistModerationEvent> builder)
    {
        builder.ToTable(
            "wishlist_moderation_events",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_wishlist_moderation_events_action",
                    "action IN ('Suspended', 'ReasonUpdated', 'Reactivated')");
                table.HasCheckConstraint(
                    "ck_wishlist_moderation_events_reason",
                    "(action = 'Reactivated' AND reason IS NULL) OR " + "(action IN ('Suspended', 'ReasonUpdated') AND reason IS NOT NULL AND char_length(btrim(reason)) > 0)");
                table.HasCheckConstraint(
                    "ck_wishlist_moderation_events_sequence",
                    "sequence > 0");
            });
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => new
        {
            value.OccurredAt,
            value.Id
        });
        builder
            .Property(value => value.Action)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder
            .Property(value => value.Reason)
            .HasMaxLength(WishlistModerationValidation.MaximumReasonLength);
        builder
            .HasOne<Wishlist>()
            .WithMany()
            .HasForeignKey(value => value.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(value => value.AdministratorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasIndex(value => new { value.WishlistId, value.Sequence })
            .IsUnique();
    }
}
