using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class NonPostgreSqlDbUpdateUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new DbUpdateException("The non-PostgreSQL update failed.");
    }
}
