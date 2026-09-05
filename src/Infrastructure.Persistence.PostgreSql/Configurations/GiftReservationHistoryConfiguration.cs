using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures durable member reservation history for PostgreSQL.
/// </summary>
public class GiftReservationHistoryConfiguration : IEntityTypeConfiguration<GiftReservationHistory>
{
    private const int MaximumStatusLength = 16;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GiftReservationHistory> builder)
    {
        builder.ToTable("gift_reservation_histories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.WishlistName)
            .HasMaxLength(WishlistTextValidation.MaximumNameLength)
            .IsRequired();
        builder.Property(history => history.WishName)
            .HasMaxLength(WishTextValidation.MaximumNameLength)
            .IsRequired();
        builder.Property(history => history.Quantity)
            .IsRequired();
        builder.Property(history => history.Status)
            .HasConversion<string>()
            .HasMaxLength(MaximumStatusLength)
            .IsRequired();
        builder.Property(history => history.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(history => history.LastActivityAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(history => history.EndedAt)
            .HasColumnType("timestamp with time zone");
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(history => history.MemberId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_gift_reservation_histories_users_member_id");
        builder.HasIndex(history => new
        {
            history.MemberId,
            history.LastActivityAt,
            history.Id
        })
            .IsDescending(
                false,
                true,
                true)
            .HasDatabaseName("ix_gift_reservation_histories_member_activity");
        builder.HasIndex(history => new
        {
            history.MemberId,
            history.Status,
            history.LastActivityAt,
            history.Id
        })
            .IsDescending(
                false,
                false,
                true,
                true)
            .HasDatabaseName("ix_gift_reservation_histories_member_status_activity");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_gift_reservation_histories_wishlist_name_valid",
                "char_length(btrim(wishlist_name)) > 0 AND wishlist_name !~ '[[:cntrl:]]'");
            table.HasCheckConstraint(
                "ck_gift_reservation_histories_wish_name_valid",
                "char_length(btrim(wish_name)) > 0 AND wish_name !~ '[[:cntrl:]]'");
            table.HasCheckConstraint(
                "ck_gift_reservation_histories_quantity_valid",
                $"quantity BETWEEN {WishTextValidation.MinimumQuantity} AND {WishTextValidation.MaximumQuantity}");
            table.HasCheckConstraint(
                "ck_gift_reservation_histories_status_valid",
                "status IN ('Active', 'Cancelled', 'Unavailable')");
            table.HasCheckConstraint(
                "ck_gift_reservation_histories_lifecycle_consistent",
                "last_activity_at >= created_at AND " +
                "((status = 'Active' AND ended_at IS NULL) OR " +
                "(status IN ('Cancelled', 'Unavailable') AND ended_at = last_activity_at))");
        });
    }
}
