using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

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

    public AuthenticationEmailDeliveryPolicy? Policy
    {
        get; private set;
    }

    public Action<int>? OnCall
    {
        get; init;
    }

    public Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        AuthenticationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken)
    {
        FrontendOrigin = frontendOrigin;
        Policy = policy;
        _callCount++;
        OnCall?.Invoke(_callCount);
        Called.TrySetResult();

        return exception is null
            ? Task.FromResult(0)
            : Task.FromException<int>(exception);
    }
}
