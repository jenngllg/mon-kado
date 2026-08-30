using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures gift wishes for PostgreSQL.
/// </summary>
public class WishConfiguration : IEntityTypeConfiguration<Wish>
{
    /// <summary>
    /// Configures gift wish persistence.
    /// </summary>
    /// <param name="builder">The entity type builder.</param>
    public void Configure(EntityTypeBuilder<Wish> builder)
    {
        builder.ToTable("wishes");
        builder.HasKey(wish => wish.Id);
        builder.HasAlternateKey(wish => new
        {
            wish.WishlistId,
            wish.Id
        });
        builder.Property(wish => wish.Name)
            .HasMaxLength(WishTextValidation.MaximumNameLength)
            .IsRequired();
        builder.Property(wish => wish.Note)
            .HasMaxLength(WishTextValidation.MaximumNoteLength);
        builder.Property(wish => wish.Url)
            .HasMaxLength(WishTextValidation.MaximumUrlLength);
        builder.Property(wish => wish.Price)
            .HasPrecision(
                WishTextValidation.MaximumPricePrecision,
                WishTextValidation.MaximumPriceScale);
        builder.Property(wish => wish.Quantity)
            .IsRequired();
        builder.Property(wish => wish.Position)
            .IsRequired();
        builder.Property(wish => wish.Version)
            .IsRowVersion();

        builder.HasOne<Wishlist>()
            .WithMany()
            .HasForeignKey(wish => wish.WishlistId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_wishes_wishlists_wishlist_id");
        builder.HasIndex(wish => new
        {
            wish.WishlistId,
            wish.Position
        })
            .HasDatabaseName("ux_wishes_wishlist_position")
            .IsUnique();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_wishes_name_valid",
                "char_length(btrim(name)) > 0 AND name !~ '[[:cntrl:]]'");
            table.HasCheckConstraint(
                "ck_wishes_url_valid",
                "url IS NULL OR (url ~* '^https?://[^[:space:]]+$' AND position('@' in split_part(split_part(split_part(url, '/', 3), '?', 1), '#', 1)) = 0)");
            table.HasCheckConstraint(
                "ck_wishes_price_valid",
                "price IS NULL OR price > 0");
            table.HasCheckConstraint(
                "ck_wishes_quantity_valid",
                $"quantity BETWEEN {WishTextValidation.MinimumQuantity} AND {WishTextValidation.MaximumQuantity}");
            table.HasCheckConstraint(
                "ck_wishes_position_valid",
                "position > 0");
            table.HasCheckConstraint(
                "ck_wishes_timestamps_consistent",
                "updated_at IS NULL OR updated_at >= created_at");
        });
    }
}
