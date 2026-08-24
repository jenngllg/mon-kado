using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingGoogleAccountSessionService(TimeProvider timeProvider)
    : IGoogleAccountSessionService
{
    private readonly ConcurrentDictionary<Guid, byte> _consumedFlows = new();

    public Guid? ExpectedMemberId
    {
        get; set;
    }

    public bool IsResolutionUnavailable
    {
        get; set;
    }

    public bool IsCompletionUnavailable
    {
        get; set;
    }

    public bool IsCompletionRejected
    {
        get; set;
    }

    public bool ReturnNullCompletionSession
    {
        get; set;
    }

    public bool IsLinkUnavailable
    {
        get; set;
    }

    public GoogleAuthenticationOutcome CompletionOutcome
    {
        get; set;
    } =
        GoogleAuthenticationOutcome.SessionCreated;

    public GoogleAccountLinkOutcome LinkOutcome { get; set; } = GoogleAccountLinkOutcome.Success;

    public GoogleAuthenticationContext? LastCompletionContext
    {
        get; private set;
    }

    public GoogleAuthenticationContext? LastLinkContext
    {
        get; private set;
    }

    public string? LastCurrentPassword
    {
        get; private set;
    }

    public GoogleIdentity? LastResolvedIdentity
    {
        get; private set;
    }

    public int ResolveCallCount
    {
        get; private set;
    }

    public int LinkCallCount
    {
        get; private set;
    }

    public int CompletionCallCount
    {
        get; private set;
    }

    public Task<Guid?> ResolveExpectedMemberIdAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastResolvedIdentity = identity;
        ResolveCallCount++;

        return IsResolutionUnavailable
            ? throw new DependencyUnavailableException(
                "PostgreSQL",
                null)
            : Task.FromResult(ExpectedMemberId);
    }

    public Task<GoogleAuthenticationResult> CompleteAsync(
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastCompletionContext = authenticationContext;
        CompletionCallCount++;

        if (IsCompletionUnavailable)
            throw new DependencyUnavailableException(
                "PostgreSQL",
                null);

        if (IsCompletionRejected)
            throw new GoogleAuthenticationFailedException();

        if (CompletionOutcome == GoogleAuthenticationOutcome.SessionCreated &&
            !_consumedFlows.TryAdd(
                authenticationContext.FlowId,
                0))
            throw new GoogleAuthenticationFailedException();

        var session = CompletionOutcome == GoogleAuthenticationOutcome.SessionCreated &&
            !ReturnNullCompletionSession
            ? new AccountRefreshSession(
                "functional-google-refresh",
                timeProvider.GetUtcNow().UtcDateTime.AddDays(30),
                authenticationContext.IsPersistent)
            : null;

        return Task.FromResult(new GoogleAuthenticationResult(
            CompletionOutcome,
            session));
    }

    public Task<GoogleAccountLinkResult> LinkAsync(
        GoogleAuthenticationContext authenticationContext,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastLinkContext = authenticationContext;
        LastCurrentPassword = currentPassword;
        LinkCallCount++;

        if (IsLinkUnavailable)
            throw new DependencyUnavailableException(
                "PostgreSQL",
                null);

        if (LinkOutcome == GoogleAccountLinkOutcome.Success &&
            !_consumedFlows.TryAdd(
                authenticationContext.FlowId,
                0))
            throw new GoogleAuthenticationFailedException();

        var tokens = LinkOutcome == GoogleAccountLinkOutcome.Success
            ? new AccountSessionTokens(
                new AccessToken(
                    "functional-access-token",
                    900),
                "functional-linked-refresh",
                timeProvider.GetUtcNow().UtcDateTime.AddDays(30),
                authenticationContext.IsPersistent)
            : null;

        return Task.FromResult(new GoogleAccountLinkResult(
            LinkOutcome,
            tokens));
    }
}
