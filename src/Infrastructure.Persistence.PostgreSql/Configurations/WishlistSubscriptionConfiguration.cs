using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Configures unique, revocable member subscriptions.</summary>
public class WishlistSubscriptionConfiguration : IEntityTypeConfiguration<WishlistSubscription>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WishlistSubscription> builder)
    {
        builder.ToTable(
            "wishlist_subscriptions",
            table => table.HasCheckConstraint(
                "ck_wishlist_subscriptions_secret_hash",
                "octet_length(share_secret_hash) = 32"));
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.ShareSecretHash)
            .IsRequired();
        builder.HasIndex(subscription => new
        {
            subscription.MemberId,
            subscription.WishlistId
        })
            .IsUnique()
            .HasDatabaseName("ux_wishlist_subscriptions_member_wishlist");
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(subscription => subscription.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Wishlist>()
            .WithMany()
            .HasForeignKey(subscription => subscription.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WishlistShareLink>()
            .WithMany()
            .HasForeignKey(subscription => subscription.ShareLinkId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
