namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IExpiredAccountCleanup
{
    Task<int> DeleteExpiredUnconfirmedAccountsAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
