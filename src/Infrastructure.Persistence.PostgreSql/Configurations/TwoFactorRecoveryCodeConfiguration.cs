using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps hashed recovery credentials with expiring exclusive reservations.</summary>
public class TwoFactorRecoveryCodeConfiguration : IEntityTypeConfiguration<TwoFactorRecoveryCode>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TwoFactorRecoveryCode> builder)
    {
        builder.ToTable(
            "two_factor_recovery_codes",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_two_factor_recovery_codes_hash_length",
                    "octet_length(code_hash) = 32");
                table.HasCheckConstraint(
                    "ck_two_factor_recovery_codes_reservation_consistent",
                    "(reserved_challenge_id IS NULL) = (reserved_until IS NULL)");
                table.HasCheckConstraint(
                    "ck_two_factor_recovery_codes_consumption_reserved",
                    "consumed_at IS NULL OR reserved_challenge_id IS NOT NULL");
            });
        builder.HasKey(code => code.Id);
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(code => code.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(code => new
        {
            code.MemberId,
            code.CodeHash
        })
            .IsUnique();
    }
}
