using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records current session service calls for functional tests.
/// </summary>
public class RecordingCurrentSessionService : ICurrentSessionService
{
    /// <summary>
    /// Gets the requested member identifiers.
    /// </summary>
    public List<Guid> MemberIds { get; } = [];

    /// <summary>
    /// Gets or sets the current session returned by the fake.
    /// </summary>
    public CurrentSession? CurrentSession
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the exception thrown by the fake.
    /// </summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <summary>
    /// Gets the current session configured for the test.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The configured current session.</returns>
    public Task<CurrentSession?> GetAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MemberIds.Add(memberId);

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(CurrentSession);
    }
}
