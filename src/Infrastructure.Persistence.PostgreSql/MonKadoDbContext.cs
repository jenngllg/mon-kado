using System.Reflection;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

public sealed class MonKadoDbContext(DbContextOptions<MonKadoDbContext> options)
    : IdentityDbContext<MonKadoUser, IdentityRole<Guid>, Guid>(options)
{
    private static readonly Assembly PersistenceAssembly = typeof(MonKadoDbContext).Assembly;
    public DbSet<AuthenticationSession> AuthenticationSessions =>
        Set<AuthenticationSession>();


    public DbSet<AuthenticationEmailOutboxMessage> AuthenticationEmailOutboxMessages =>
        Set<AuthenticationEmailOutboxMessage>();

    private static readonly bool HasEntityTypeConfigurations = PersistenceAssembly.DefinedTypes.Any(type =>
        !type.IsAbstract &&
        !type.IsGenericTypeDefinition &&
        type.ImplementedInterfaces.Any(@interface =>
            @interface.IsGenericType &&
            @interface.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");

        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        if (HasEntityTypeConfigurations)
        {
            builder.ApplyConfigurationsFromAssembly(PersistenceAssembly);
        }

        IdentityModelConfiguration.Configure(builder);
    }
}
