using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class WishlistShareLinkConfiguration : IEntityTypeConfiguration<WishlistShareLink>
{
    private const int SecretHashLength = 32;
    private const int MaximumProtectedSecretLength = 1024;

    public void Configure(EntityTypeBuilder<WishlistShareLink> builder)
    {
        builder.ToTable("wishlist_share_links");
        builder.HasKey(shareLink => shareLink.Id);
        builder.Property(shareLink => shareLink.SecretHash)
            .HasMaxLength(SecretHashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(shareLink => shareLink.ProtectedSecret)
            .HasMaxLength(MaximumProtectedSecretLength)
            .IsRequired();
        builder.Property(shareLink => shareLink.Version)
            .IsRowVersion();
        builder.HasOne<Wishlist>()
            .WithOne()
            .HasForeignKey<WishlistShareLink>(shareLink => shareLink.WishlistId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wishlist_share_links_wishlists_wishlist_id");
        builder.HasIndex(shareLink => shareLink.WishlistId)
            .HasDatabaseName("ux_wishlist_share_links_wishlist_id")
            .IsUnique();
        builder.HasIndex(shareLink => shareLink.SecretHash)
            .HasDatabaseName("ux_wishlist_share_links_secret_hash")
            .IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_wishlist_share_links_secret_hash_length",
                "octet_length(secret_hash) = 32");
            table.HasCheckConstraint(
                "ck_wishlist_share_links_timestamps_consistent",
                "updated_at IS NULL OR updated_at >= created_at");
        });
    }
}
