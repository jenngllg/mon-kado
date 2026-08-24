using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class ThrowingProcessedAuthenticationEmailCleanup(Exception exception)
    : IProcessedAuthenticationEmailCleanup
{
    public Action? OnCall
    {
        get; init;
    }

    public Task<int> DeleteProcessedEmailsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        OnCall?.Invoke();

        return Task.FromException<int>(exception);
    }
}
