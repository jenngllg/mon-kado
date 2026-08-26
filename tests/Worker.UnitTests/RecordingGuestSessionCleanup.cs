using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class RecordingGuestSessionCleanup(params int[] results) : IExpiredGuestSessionCleanup
{
    private readonly Queue<int> _remainingResults = new(results);

    public List<int> BatchSizes { get; } = [];

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
        int batchSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BatchSizes.Add(batchSize);
        OnCall?.Invoke(BatchSizes.Count);
        Called.TrySetResult();

        return Task.FromResult(_remainingResults.Dequeue());
    }
}
