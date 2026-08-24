using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

internal static class IdentityModelConfiguration
{
    private const int MaximumLoginProviderLength = 128;
    private const int MaximumProviderKeyLength = 255;
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<IdentityRole<Guid>>(role =>
        {
            role.HasIndex(value => value.NormalizedName)
                .HasDatabaseName("ux_roles_normalized_name")
                .IsUnique();
            role.HasData(new IdentityRole<Guid>
            {
                ConcurrencyStamp = "0198d027-51c0-7000-8000-000000000003",
                Id = RoleIds.Member,
                Name = RoleNames.Member,
                NormalizedName = RoleNames.Member.ToUpperInvariant()
            });
        });

        builder.Entity<IdentityUserClaim<Guid>>()
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(claim => claim.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_claims_users_user_id");

        builder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.Property(value => value.LoginProvider).HasMaxLength(MaximumLoginProviderLength);
            login.Property(value => value.ProviderKey).HasMaxLength(MaximumProviderKeyLength);
            login.HasIndex(value => new
            {
                value.UserId,
                value.LoginProvider
            })
                .HasDatabaseName("ux_user_logins_user_id_login_provider")
                .IsUnique();
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
            token.Property(value => value.LoginProvider).HasMaxLength(MaximumLoginProviderLength);
            token.Property(value => value.Name).HasMaxLength(MaximumLoginProviderLength);
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
