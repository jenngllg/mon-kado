using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class WishPositionSequenceConfiguration : IEntityTypeConfiguration<WishPositionSequence>
{
    /// <summary>
    /// Configures gift wish collection state persistence.
    /// </summary>
    /// <param name="builder">The entity type builder.</param>
    public void Configure(EntityTypeBuilder<WishPositionSequence> builder)
    {
        builder.ToTable("wish_position_sequences");
        builder.HasKey(sequence => sequence.WishlistId);
        builder.Property(sequence => sequence.NextPosition)
            .IsRequired();
        builder.Property(sequence => sequence.CurrentCount)
            .IsRequired();
        builder.Property(sequence => sequence.Version)
            .IsRowVersion();

        builder.HasOne<Wishlist>()
            .WithOne()
            .HasForeignKey<WishPositionSequence>(sequence => sequence.WishlistId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wish_position_sequences_wishlists_wishlist_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_wish_position_sequences_next_position_valid",
                "next_position >= 0");
            table.HasCheckConstraint(
                "ck_wish_position_sequences_current_count_valid",
                "current_count >= 0 AND current_count <= 1000");
        });
    }
}
