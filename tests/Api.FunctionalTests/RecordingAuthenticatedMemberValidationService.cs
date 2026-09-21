using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>Substitutes only the database account-existence boundary in HTTP contract tests.</summary>
public class RecordingAuthenticatedMemberValidationService : IAuthenticatedMemberValidationService
{
    private int _calls;

    /// <summary>Gets the number of authentication database-boundary calls.</summary>
    public int Calls => System.Threading.Volatile.Read(ref _calls);
    /// <summary>Gets or sets the database-boundary failure to simulate.</summary>
    public Exception? Failure
    {
        get; set;
    }

    /// <inheritdoc/>
    public Task ValidateAsync(
        Guid memberId,
        Guid tokenId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _calls);

        if (Failure is not null)
            return Task.FromException(Failure);

        return Task.CompletedTask;
    }
}
