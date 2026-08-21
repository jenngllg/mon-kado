using JennGllg.Fr.MonKado.Back.Application.Abstractions;
namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class RecordingCleanup(params int[] results) : IExpiredAuthenticationSessionCleanup
{
    private readonly Queue<int> _remainingResults = new(results);

    public List<CleanupCall> Calls { get; } = [];

    public Action<int>? OnCall
    {
        get; init;
    }

    public TaskCompletionSource Called
    {
        get;
    } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new CleanupCall(
            cutoff,
            batchSize));
        OnCall?.Invoke(Calls.Count);
        Called.TrySetResult();

        return Task.FromResult(_remainingResults.Dequeue());
    }
}
