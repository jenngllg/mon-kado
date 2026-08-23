namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class ConcurrentEmailChangeCoordinator
{
    private readonly TaskCompletionSource<bool> _bothConfirmationsStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _startedConfirmationCount;

    public async Task WaitAsync()
    {
        if (Interlocked.Increment(ref _startedConfirmationCount) == 2)
            _bothConfirmationsStarted.TrySetResult(true);

        await _bothConfirmationsStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
