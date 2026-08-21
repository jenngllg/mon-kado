using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingSessionCleanup(Exception exception) : IExpiredAuthenticationSessionCleanup
{
    public Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        return Task.FromException<int>(exception);
    }
}
