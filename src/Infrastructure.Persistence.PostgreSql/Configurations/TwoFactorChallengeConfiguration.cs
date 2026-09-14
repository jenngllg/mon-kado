using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps short-lived hashed proofs and exact non-secret completion receipts.</summary>
public class TwoFactorChallengeConfiguration : IEntityTypeConfiguration<TwoFactorChallenge>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TwoFactorChallenge> builder)
    {
        builder.ToTable(
            "two_factor_challenges",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_two_factor_challenges_hash_lengths",
                    "octet_length(flow_hash) = 32 AND octet_length(security_stamp_hash) = 32");
                table.HasCheckConstraint(
                    "ck_two_factor_challenges_absolute_expiration",
                    "expires_at = created_at + interval '5 minutes'");
                table.HasCheckConstraint(
                    "ck_two_factor_challenges_candidate_consistent",
                    "(pending_credential_id IS NULL) = (pending_protected_secret IS NULL)");
                table.HasCheckConstraint(
                    "ck_two_factor_challenges_receipt_consistent",
                    "(result_session_id IS NULL) = (result_access_token_id IS NULL)");
                table.HasCheckConstraint(
                    "ck_two_factor_challenges_purpose_valid",
                    "purpose IN ('SignIn', 'ReplaceAuthenticator', 'RegenerateRecoveryCodes')");
                table.HasCheckConstraint(
                    "ck_two_factor_challenges_action_valid",
                    "required_action IN ('Verify', 'Enroll', 'Replace', 'Complete')");
            });
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Purpose)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(challenge => challenge.RequiredAction)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(challenge => challenge.PendingProtectedSecret)
            .HasMaxLength(4096);
        builder.Property(challenge => challenge.ProtectedGoogleContext)
            .HasMaxLength(16384);
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(challenge => challenge.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(challenge => challenge.FlowHash)
            .IsUnique();
        builder.HasIndex(challenge => challenge.GoogleFlowId)
            .IsUnique()
            .HasFilter("google_flow_id IS NOT NULL");
        builder.HasIndex(challenge => new
        {
            challenge.ExpiresAt,
            challenge.Id
        });
    }
}
