using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingAccountCleanup(Exception exception) : IExpiredAccountCleanup
{
    public Action? OnCall
    {
        get; init;
    }

    public Task<int> DeleteExpiredUnconfirmedAccountsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        OnCall?.Invoke();

        return Task.FromException<int>(exception);
    }
}
