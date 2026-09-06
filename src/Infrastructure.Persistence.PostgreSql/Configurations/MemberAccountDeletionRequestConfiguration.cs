using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Configures account deletion confirmation requests.</summary>
public class MemberAccountDeletionRequestConfiguration : IEntityTypeConfiguration<MemberAccountDeletionRequest>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<MemberAccountDeletionRequest> builder)
    {
        builder.ToTable(
            "member_account_deletion_requests",
            table => table.HasCheckConstraint(
                "ck_member_account_deletion_requests_expiration",
                "expires_at > created_at"));
        builder.HasKey(request => request.Id);
        builder
            .Property(request => request.Email)
            .HasMaxLength(254)
            .IsRequired();
        builder
            .Property(request => request.SecurityStamp)
            .HasMaxLength(256)
            .IsRequired();
        builder
            .Property(request => request.CreatedAt)
            .HasColumnType("timestamp with time zone");
        builder
            .Property(request => request.ExpiresAt)
            .HasColumnType("timestamp with time zone");
        builder
            .HasIndex(request => request.MemberId)
            .IsUnique();
        builder.HasIndex(request => request.ExpiresAt);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(request => request.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
