using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal static class IdentityModelConfiguration
{
    private const int MaximumProviderKeyLength = 128;

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<IdentityRole<Guid>>()
            .HasIndex(role => role.NormalizedName)
            .HasDatabaseName("ux_roles_normalized_name")
            .IsUnique();

        builder.Entity<IdentityUserClaim<Guid>>()
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(claim => claim.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_claims_users_user_id");

        builder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.Property(value => value.LoginProvider).HasMaxLength(MaximumProviderKeyLength);
            login.Property(value => value.ProviderKey).HasMaxLength(MaximumProviderKeyLength);
            login.HasOne<MonKadoUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_logins_users_user_id");
        });

        builder.Entity<IdentityUserRole<Guid>>(userRole =>
        {
            userRole.HasOne<MonKadoUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_roles_users_user_id");
            userRole.HasOne<IdentityRole<Guid>>()
                .WithMany()
                .HasForeignKey(value => value.RoleId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_roles_roles_role_id");
        });

        builder.Entity<IdentityUserToken<Guid>>(token =>
        {
            token.Property(value => value.LoginProvider).HasMaxLength(MaximumProviderKeyLength);
            token.Property(value => value.Name).HasMaxLength(MaximumProviderKeyLength);
            token.HasOne<MonKadoUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_tokens_users_user_id");
        });

        builder.Entity<IdentityRoleClaim<Guid>>()
            .HasOne<IdentityRole<Guid>>()
            .WithMany()
            .HasForeignKey(claim => claim.RoleId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_role_claims_roles_role_id");
    }
}
