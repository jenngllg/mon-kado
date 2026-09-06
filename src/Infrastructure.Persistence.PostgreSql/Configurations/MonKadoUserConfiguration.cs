using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal sealed class MonKadoUserConfiguration : IEntityTypeConfiguration<MonKadoUser>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<MonKadoUser> builder)
    {
        builder.ToTable("users");

        builder.Property(user => user.Email)
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.UserName)
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.NormalizedUserName)
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.DisplayName)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(user => user.Version)
            .IsRowVersion();
        builder.HasIndex(user => user.ProfileImageId)
            .HasDatabaseName("ux_users_profile_image_id")
            .IsUnique()
            .HasFilter("profile_image_id IS NOT NULL");

        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("ux_users_normalized_email")
            .IsUnique();
        builder.HasIndex(user => user.NormalizedUserName)
            .HasDatabaseName("ux_users_normalized_user_name")
            .IsUnique();
        builder.HasIndex(user => user.UnconfirmedAccountExpiresAt)
            .HasDatabaseName("ix_users_unconfirmed_account_expiry")
            .HasFilter("email_confirmed = FALSE AND unconfirmed_account_expires_at IS NOT NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_users_profile_image_consistent",
                "(profile_image_id IS NULL AND profile_image_hash IS NULL) OR " +
                "(profile_image_id IS NOT NULL AND profile_image_hash IS NOT NULL AND octet_length(profile_image_hash) = 32)");
            table.HasCheckConstraint(
                "ck_users_display_name_valid",
                "char_length(btrim(display_name)) > 0 AND display_name !~ '[[:cntrl:]]'");
            table.HasCheckConstraint(
                "ck_users_timestamps_consistent",
                "updated_at >= created_at AND " +
                "(unconfirmed_account_expires_at IS NULL OR " +
                "unconfirmed_account_expires_at >= created_at)");
        });
    }
}
