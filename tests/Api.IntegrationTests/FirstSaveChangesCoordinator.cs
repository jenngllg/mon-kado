namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class FirstSaveChangesCoordinator
{
    private readonly TaskCompletionSource _firstSaveStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseFirstSave = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _saveCount;

    public async Task WaitBeforeSaveAsync(CancellationToken cancellationToken)
    {
        var saveCount = Interlocked.Increment(ref _saveCount);

        if (saveCount != 1)
            return;

        _firstSaveStarted.TrySetResult();
        await _releaseFirstSave.Task.WaitAsync(cancellationToken);
    }

    public async Task WaitUntilFirstSaveStartsAsync(CancellationToken cancellationToken)
    {
        await _firstSaveStarted.Task.WaitAsync(cancellationToken);
    }

    public void ReleaseFirstSave()
    {
        _releaseFirstSave.TrySetResult();
    }
}
