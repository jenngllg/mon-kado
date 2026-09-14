using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps encrypted authenticator state and account-wide verification defenses.</summary>
public class MemberTwoFactorConfiguration : IEntityTypeConfiguration<MemberTwoFactor>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<MemberTwoFactor> builder)
    {
        builder.ToTable(
            "member_two_factors",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_member_two_factors_credential_consistent",
                    "(credential_id IS NULL AND protected_secret IS NULL AND enabled_at IS NULL AND last_accepted_time_step IS NULL) OR " +
                    "(credential_id IS NOT NULL AND protected_secret IS NOT NULL AND enabled_at IS NOT NULL AND last_accepted_time_step IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_member_two_factors_attempts_nonnegative",
                    "failed_attempts >= 0 AND verification_count >= 0");
            });
        builder.HasKey(factor => factor.MemberId);
        builder.Property(factor => factor.ProtectedSecret)
            .HasMaxLength(4096);
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(factor => factor.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
