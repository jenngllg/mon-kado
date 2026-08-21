using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class RecordingDispatcher(Exception? exception = null) : IAuthenticationEmailDispatcher
{
    private int _callCount;

    public TaskCompletionSource Called
    {
        get;
    } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Uri? FrontendOrigin
    {
        get; private set;
    }

    public int BatchSize
    {
        get; private set;
    }

    public TimeSpan LeaseDuration
    {
        get; private set;
    }

    public Action<int>? OnCall
    {
        get; init;
    }

    public Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        FrontendOrigin = frontendOrigin;
        BatchSize = batchSize;
        LeaseDuration = leaseDuration;
        _callCount++;
        OnCall?.Invoke(_callCount);
        Called.TrySetResult();

        return exception is null
            ? Task.FromResult(0)
            : Task.FromException<int>(exception);
    }
}
