using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures gift reservations for PostgreSQL.
/// </summary>
public class GiftReservationConfiguration : IEntityTypeConfiguration<GiftReservation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GiftReservation> builder)
    {
        builder.ToTable(
            "gift_reservations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_gift_reservations_quantity_valid",
                    $"quantity BETWEEN {WishTextValidation.MinimumQuantity} AND {WishTextValidation.MaximumQuantity}");
                table.HasCheckConstraint(
                    "ck_gift_reservations_timestamps_consistent",
                    "updated_at IS NULL OR updated_at >= created_at");
            });
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Quantity)
            .IsRequired();
        builder.Property(reservation => reservation.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reservation => reservation.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.Property(reservation => reservation.Version)
            .IsRowVersion();
        builder.HasIndex(reservation => new
        {
            reservation.WishlistParticipantId,
            reservation.WishId
        })
            .IsUnique()
            .HasDatabaseName("ux_gift_reservations_participant_wish");
        builder.HasOne<Wish>()
            .WithMany()
            .HasForeignKey(reservation => new
            {
                reservation.WishlistId,
                reservation.WishId
            })
            .HasPrincipalKey(wish => new
            {
                wish.WishlistId,
                wish.Id
            })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_gift_reservations_wishes_wishlist_id_wish_id");
        builder.HasOne<WishlistParticipant>()
            .WithMany()
            .HasForeignKey(reservation => new
            {
                reservation.WishlistId,
                reservation.WishlistParticipantId
            })
            .HasPrincipalKey(participant => new
            {
                participant.WishlistId,
                participant.Id
            })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_gift_reservations_participants_wishlist_id_participant_id");
    }
}
