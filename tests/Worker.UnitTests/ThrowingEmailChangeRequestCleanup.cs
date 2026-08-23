using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingEmailChangeRequestCleanup(Exception exception)
    : IExpiredMemberEmailChangeRequestCleanup
{
    public Action? OnCall
    {
        get; init;
    }

    public Task<int> DeleteExpiredRequestsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        OnCall?.Invoke();

        return Task.FromException<int>(exception);
    }
}
