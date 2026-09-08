using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Stages account data removal without committing or performing external I/O.</summary>
public interface IMemberAccountDataRemovalService
{
    /// <summary>Locks dependent resources and stages deletion within an existing transaction.</summary>
    /// <param name="member">The account already locked by the caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the staged deletion.</returns>
    Task StageAsync(
        MonKadoUser member,
        CancellationToken cancellationToken);
}
