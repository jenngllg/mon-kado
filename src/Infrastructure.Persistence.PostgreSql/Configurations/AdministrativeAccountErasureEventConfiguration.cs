using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps a minimal retained erasure audit independently of the removed target.</summary>
public class AdministrativeAccountErasureEventConfiguration : IEntityTypeConfiguration<AdministrativeAccountErasureEvent>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AdministrativeAccountErasureEvent> builder)
    {
        builder.ToTable(
            "administrative_account_erasure_events",
            table => table.HasCheckConstraint(
                "ck_administrative_account_erasure_events_notification_status",
                "notification_status IN ('NotApplicable', 'Pending', 'Accepted', 'Failed')"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.RequestReference)
            .HasMaxLength(AdministrativeAccountErasureConstraints.MaximumReferenceLength);
        builder.Property(value => value.NotificationStatus)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.HasOne<MonKadoUser>()
            .WithMany()
            .HasForeignKey(value => value.AdministratorId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(value => new
        {
            value.CreatedAt,
            value.Id
        });
    }
}
