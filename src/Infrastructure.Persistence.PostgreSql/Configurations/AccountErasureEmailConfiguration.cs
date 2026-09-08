using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps short-lived notification recipients without an account dependency.</summary>
public class AccountErasureEmailConfiguration : IEntityTypeConfiguration<AccountErasureEmail>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AccountErasureEmail> builder)
    {
        builder.ToTable(
            "account_erasure_email_outbox",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_account_erasure_email_attempts",
                    "attempt_count >= 0");
                table.HasCheckConstraint(
                    "ck_account_erasure_email_lease",
                    "(lease_id IS NULL) = (locked_until IS NULL)");
                table.HasCheckConstraint(
                    "ck_account_erasure_email_dates",
                    "expires_at = created_at + interval '24 hours' AND available_at >= created_at");
            });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ProtectedRecipient)
            .HasMaxLength(4096);
        builder.HasOne<AdministrativeAccountErasureEvent>()
            .WithOne()
            .HasForeignKey<AccountErasureEmail>(value => value.Id)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new
        {
            value.AvailableAt,
            value.Id
        });
        builder.HasIndex(value => new
        {
            value.ExpiresAt,
            value.Id
        });
    }
}
