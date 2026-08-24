namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Coordinates the request snapshot read and release of an in-flight email dispatcher.
/// </summary>
public class EmailChangeRequestReadCoordinator
{
    private readonly TaskCompletionSource _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _requestRead = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Signals that the dispatcher has read the request snapshot.
    /// </summary>
    public void SignalRequestRead()
    {
        _requestRead.TrySetResult();
    }

    /// <summary>
    /// Waits until the dispatcher has read the request snapshot.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WaitUntilRequestReadAsync(CancellationToken cancellationToken)
    {

        return _requestRead.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Allows the suspended request read to complete.
    /// </summary>
    public void Release()
    {
        _release.TrySetResult();
    }

    /// <summary>
    /// Waits until the account claim allows the dispatcher to continue.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WaitUntilReleasedAsync(CancellationToken cancellationToken)
    {

        return _release.Task.WaitAsync(cancellationToken);
    }
}
