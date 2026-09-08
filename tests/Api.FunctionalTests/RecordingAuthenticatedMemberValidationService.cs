using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>Substitutes only the database account-existence boundary in HTTP contract tests.</summary>
public class RecordingAuthenticatedMemberValidationService : IAuthenticatedMemberValidationService
{
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

        if (Failure is not null)
            return Task.FromException(Failure);

        return Task.CompletedTask;
    }
}
