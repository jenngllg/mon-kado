using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    private const int MaximumOccasionLength = 16;

    /// <summary>
    /// Configures private wishlist persistence.
    /// </summary>
    /// <param name="builder">The entity type builder.</param>
    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.ToTable("wishlists");
        builder.HasKey(wishlist => wishlist.Id);
        builder.Property(wishlist => wishlist.Name)
            .HasMaxLength(WishlistTextValidation.MaximumNameLength)
            .IsRequired();
        builder.Property(wishlist => wishlist.NormalizedName)
            .HasMaxLength(WishlistTextValidation.MaximumNameLength)
            .IsRequired();
        builder.Property(wishlist => wishlist.Occasion)
            .HasConversion<string>()
            .HasMaxLength(MaximumOccasionLength)
            .IsRequired();
        builder.Property(wishlist => wishlist.EventDate)
            .HasColumnType("date");
        builder.Property(wishlist => wishlist.Message)
            .HasMaxLength(WishlistTextValidation.MaximumMessageLength);
        builder.Property(wishlist => wishlist.Version)
            .IsRowVersion();

        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(wishlist => wishlist.OwnerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wishlists_users_owner_id");
        builder.HasIndex(wishlist => new
        {
            wishlist.OwnerId,
            wishlist.NormalizedName
        })
            .HasDatabaseName("ux_wishlists_owner_normalized_name")
            .IsUnique();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_wishlists_name_valid",
                "char_length(btrim(name)) > 0 AND name !~ '[[:cntrl:]]'");
            table.HasCheckConstraint(
                "ck_wishlists_occasion_valid",
                "occasion IN ('Birthday', 'Christmas', 'Wedding', 'Birth', 'Other')");
            table.HasCheckConstraint(
                "ck_wishlists_timestamps_consistent",
                "updated_at IS NULL OR updated_at >= created_at");
        });
    }
}
