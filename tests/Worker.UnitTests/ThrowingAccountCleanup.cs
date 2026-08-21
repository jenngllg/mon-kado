using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingAccountCleanup(Exception exception) : IExpiredAccountCleanup
{
    public Task<int> DeleteExpiredUnconfirmedAccountsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        return Task.FromException<int>(exception);
    }
}
