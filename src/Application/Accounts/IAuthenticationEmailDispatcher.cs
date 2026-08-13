namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IAuthenticationEmailDispatcher
{
    Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}
