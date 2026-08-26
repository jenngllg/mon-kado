using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingGuestSessionCleanup(Exception exception) : IExpiredGuestSessionCleanup
{
    public Action? OnCall
    {
        get; init;
    }

    public Task<int> DeleteExpiredSessionsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        _ = batchSize;
        _ = cancellationToken;
        OnCall?.Invoke();

        return Task.FromException<int>(exception);
    }
}
