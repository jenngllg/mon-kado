using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>Maps bounded JWT metadata independently of session rotation.</summary>
public class AuthenticationAccessTokenConfiguration : IEntityTypeConfiguration<AuthenticationAccessToken>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AuthenticationAccessToken> builder)
    {
        builder.ToTable("authentication_access_tokens");
        builder.HasKey(token => token.Id);
        builder
            .HasOne<AuthenticationSession>()
            .WithMany()
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(token => token.ExpiresAt);
    }
}
