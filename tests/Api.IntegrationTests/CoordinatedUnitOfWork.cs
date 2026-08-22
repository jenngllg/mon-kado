using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class CoordinatedUnitOfWork(
    MonKadoDbContext context,
    FirstSaveChangesCoordinator coordinator) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        await coordinator.WaitBeforeSaveAsync(cancellationToken);

        return await context.SaveChangesAsync(cancellationToken);
    }
}
