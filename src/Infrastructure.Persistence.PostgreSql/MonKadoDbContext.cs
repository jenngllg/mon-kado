using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

public sealed class MonKadoDbContext(DbContextOptions<MonKadoDbContext> options) : DbContext(options)
{
    private static readonly Assembly PersistenceAssembly = typeof(MonKadoDbContext).Assembly;

    private static readonly bool HasEntityTypeConfigurations = PersistenceAssembly.DefinedTypes.Any(type =>
        !type.IsAbstract &&
        !type.IsGenericTypeDefinition &&
        type.ImplementedInterfaces.Any(@interface =>
            @interface.IsGenericType &&
            @interface.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");

        if (HasEntityTypeConfigurations)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(PersistenceAssembly);
        }
    }
}
