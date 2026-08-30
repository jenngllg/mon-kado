using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Configures wishlist participants for PostgreSQL.
/// </summary>
public class WishlistParticipantConfiguration : IEntityTypeConfiguration<WishlistParticipant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WishlistParticipant> builder)
    {
        builder.ToTable(
            "wishlist_participants",
            table => table.HasCheckConstraint(
                "ck_wishlist_participants_identity",
                "member_id IS NULL OR guest_session_id IS NULL"));
        builder.HasKey(participant => participant.Id);
        builder.HasAlternateKey(participant => new
        {
            participant.WishlistId,
            participant.Id
        });
        builder.Property(participant => participant.GuestDisplayName)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(participant => participant.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(participant => participant.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.HasOne<Wishlist>()
            .WithMany()
            .HasForeignKey(participant => participant.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(participant => participant.MemberId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<GuestSession>()
            .WithMany()
            .HasForeignKey(participant => participant.GuestSessionId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(participant => new
        {
            participant.WishlistId,
            participant.MemberId
        })
            .IsUnique()
            .HasFilter("member_id IS NOT NULL")
            .HasDatabaseName("ux_wishlist_participants_wishlist_member");
        builder.HasIndex(participant => new
        {
            participant.WishlistId,
            participant.GuestSessionId
        })
            .IsUnique()
            .HasFilter("guest_session_id IS NOT NULL")
            .HasDatabaseName("ux_wishlist_participants_wishlist_guest_session");
    }
}
