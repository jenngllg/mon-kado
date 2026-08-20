namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IExpiredAuthenticationSessionCleanup
{
    Task<int> DeleteExpiredSessionsAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
