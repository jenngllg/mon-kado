using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps session-revocation audits without retaining deleted account identities.</summary>
public class AdministrativeSessionRevocationEventConfiguration : IEntityTypeConfiguration<AdministrativeSessionRevocationEvent>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AdministrativeSessionRevocationEvent> builder)
    {
        builder.ToTable("administrative_session_revocation_events");
        builder.HasKey(entry => entry.Id);
        builder
            .Property(entry => entry.RequestReference)
            .HasMaxLength(AdministrativeSessionRevocationConstraints.MaximumReferenceLength);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(entry => entry.AdministratorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(entry => entry.MemberId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(entry => new
        {
            entry.CreatedAt,
            entry.Id
        });
    }
}
