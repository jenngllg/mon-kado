using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingRefreshSessionService(TimeProvider timeProvider) : IRefreshSessionService
{
    public Guid? ProvenSessionId
    {
        get; set;
    }

    public bool IsProofUnavailable
    {
        get; set;
    }

    public string? LastRefreshToken
    {
        get; private set;
    }

    public int ProveCallCount
    {
        get; private set;
    }

    public Task<Guid?> ProveCurrentSessionAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRefreshToken = refreshToken;
        ProveCallCount++;

        return IsProofUnavailable
            ? throw new DependencyUnavailableException(
                "PostgreSQL",
                null)
            : Task.FromResult(ProvenSessionId);
    }

    public Task<AccountRefreshSession> CreateAsync(
        Guid memberId,
        bool isPersistent,
        Guid? requestedSessionId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        _ = memberId;
        _ = requestedSessionId;
        _ = currentSessionId;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AccountRefreshSession(
            "functional-refresh",
            timeProvider.GetUtcNow().UtcDateTime.AddDays(1),
            isPersistent));
    }
}
