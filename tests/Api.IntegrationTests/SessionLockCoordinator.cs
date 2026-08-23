namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class SessionLockCoordinator(
    bool coordinateLookup = false,
    bool coordinateLock = true)
{
    private readonly TaskCompletionSource _lookupCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseLookup = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _sessionLocked = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseSession = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lockCount;

    public async Task WaitAfterLookupAsync(CancellationToken cancellationToken)
    {
        if (!coordinateLookup)
            return;

        _lookupCompleted.TrySetResult();
        await _releaseLookup.Task.WaitAsync(cancellationToken);
    }

    public async Task WaitAfterLockAsync(CancellationToken cancellationToken)
    {
        if (!coordinateLock)
            return;

        var lockCount = Interlocked.Increment(ref _lockCount);

        if (lockCount != 1)
            return;

        _sessionLocked.TrySetResult();
        await _releaseSession.Task.WaitAsync(cancellationToken);
    }

    public async Task WaitUntilLookupCompletesAsync(CancellationToken cancellationToken)
    {
        await _lookupCompleted.Task.WaitAsync(cancellationToken);
    }

    public async Task WaitUntilSessionIsLockedAsync(CancellationToken cancellationToken)
    {
        await _sessionLocked.Task.WaitAsync(cancellationToken);
    }

    public void ReleaseSession()
    {
        _releaseSession.TrySetResult();
    }

    public void ReleaseLookup()
    {
        _releaseLookup.TrySetResult();
    }
}
