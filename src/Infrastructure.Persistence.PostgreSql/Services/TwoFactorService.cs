using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Queries;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Coordinates account-locked authenticator changes and one-time sign-in proofs.</summary>
/// <param name="context">The scoped transaction context.</param>
/// <param name="unitOfWork">The single-save coordinator.</param>
/// <param name="cryptography">The purpose-bound credential cryptography.</param>
/// <param name="refreshSessions">The refresh-session creator.</param>
/// <param name="accessTokens">The access-token issuer.</param>
/// <param name="scopeFactory">The independent commit-verification scope factory.</param>
/// <param name="callerProvider">The validated Bearer caller for management continuations.</param>
/// <param name="googleFinalizer">The deferred Google association coordinator.</param>
/// <param name="revocations">The account-locked security-event revocation coordinator.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class TwoFactorService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    ITwoFactorCryptography cryptography,
    IRefreshSessionService refreshSessions,
    IAccessTokenService accessTokens,
    IServiceScopeFactory scopeFactory,
    ITwoFactorCallerProvider callerProvider,
    IGoogleTwoFactorFinalizer googleFinalizer,
    ITwoFactorRevocationService revocations,
    TimeProvider timeProvider) : ITwoFactorService
{
    private const string GoogleSubjectUniqueConstraint = "pk_user_logins";
    private const string MemberGoogleLoginUniqueConstraint = "ux_user_logins_user_id_login_provider";

    /// <inheritdoc/>
    public Task<TwoFactorCompletionResult> CompleteAsync(
        string flow,
        string? code,
        string? recoveryCode,
        CancellationToken cancellationToken)
    {

        return RunFlowAsync(
            flow,
            async (state, operationCancellationToken) =>
            {
                var challenge = state.Challenge;

                if (challenge.Purpose != TwoFactorFlowPurpose.SignIn)
                    throw new TwoFactorOperationConflictException();

                if (challenge.RequiredAction == TwoFactorRequiredAction.Verify)
                {
                    StartVerification(state.Factor);

                    if (recoveryCode is not null)
                    {
                        var reservedCode = await ReserveRecoveryAsync(
                            state,
                            recoveryCode,
                            operationCancellationToken);
                        challenge.BeginRecovery(
                            reservedCode.Id,
                            Now());
                        StageNotification(
                            state,
                            AuthenticationEmailKind.TwoFactorRecoveryCodeUsed);

                        return new TwoFactorCompletionResult
                        {
                            Challenge = CreateChallengeResponse(
                                flow,
                                challenge)
                        };
                    }

                    VerifyCurrentCode(
                        state.Factor,
                        code);
                    challenge.ConfirmVerification(Now());
                }
                else if (code is not null || recoveryCode is not null)
                {

                    throw new TwoFactorOperationConflictException();
                }

                if (challenge.RequiredAction != TwoFactorRequiredAction.Complete)
                    throw new TwoFactorOperationConflictException();

                var tokens = await CreateSessionAsync(
                    state,
                    operationCancellationToken);

                return new TwoFactorCompletionResult { Tokens = tokens };
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TwoFactorSetupResponse> GetSetupAsync(
        string flow,
        CancellationToken cancellationToken)
    {

        return RunFlowAsync(
            flow,
            (state, _) =>
            {
                var challenge = state.Challenge;

                if (challenge.RequiredAction is not (TwoFactorRequiredAction.Enroll or TwoFactorRequiredAction.Replace))
                    throw new TwoFactorOperationConflictException();

                if (challenge.PendingCredentialId is null)
                {
                    var credentialId = Guid.CreateVersion7(timeProvider.GetUtcNow());
                    var secret = cryptography.CreateSecret();
                    challenge.StageAuthenticator(
                        credentialId,
                        cryptography.ProtectSecret(
                            state.Member.Id,
                            credentialId,
                            secret),
                        Now());
                }

                var pendingId = challenge.PendingCredentialId ?? throw new TwoFactorUnavailableException();
                var pendingSecret = challenge.PendingProtectedSecret ?? throw new TwoFactorUnavailableException();
                var manualKey = cryptography.UnprotectSecret(
                    state.Member.Id,
                    pendingId,
                    pendingSecret);

                return Task.FromResult(new TwoFactorSetupResponse
                {
                    ManualKey = manualKey,
                    OtpAuthUri = cryptography.CreateSetupUri(
                        manualKey,
                        state.Member.Id.ToString("D"))
                });
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TwoFactorRecoveryCodesResponse> ConfirmSetupAsync(
        string flow,
        string code,
        CancellationToken cancellationToken)
    {

        return RunFlowAsync(
            flow,
            async (state, operationCancellationToken) =>
            {
                var challenge = state.Challenge;

                if (challenge.RequiredAction is not (TwoFactorRequiredAction.Enroll or TwoFactorRequiredAction.Replace) ||
                    challenge.PendingCredentialId is not { } credentialId ||
                    challenge.PendingProtectedSecret is not { } protectedSecret)
                    throw new TwoFactorOperationConflictException();

                StartVerification(state.Factor);
                var secret = cryptography.UnprotectSecret(
                    state.Member.Id,
                    credentialId,
                    protectedSecret);
                var timeStep = cryptography.VerifyCode(
                    secret,
                    code,
                    null);

                if (timeStep is not { } matchedTimeStep)
                {
                    state.Factor.RecordFailure(Now());

                    throw new TwoFactorAuthenticationFailedException();
                }

                if (challenge.ReservedRecoveryCodeId is { } recoveryId)
                {
                    var reservedCode = await context.TwoFactorRecoveryCodes.SingleOrDefaultAsync(
                        candidate => candidate.Id == recoveryId && candidate.MemberId == state.Member.Id,
                        operationCancellationToken);

                    if (reservedCode is null || !reservedCode.TryConsume(
                            challenge.Id,
                            Now()))
                        throw new TwoFactorAuthenticationFailedException();
                }

                var notificationKind = state.Factor.CredentialId is null
                    ? AuthenticationEmailKind.TwoFactorEnrolled
                    : AuthenticationEmailKind.TwoFactorReplaced;
                state.Factor.ConfirmAuthenticator(
                    credentialId,
                    protectedSecret,
                    matchedTimeStep,
                    Now());
                state.Member.TwoFactorEnabled = true;
                challenge.ConfirmSetup(Now());
                var codes = await ReplaceRecoveryCodesAsync(
                    state,
                    operationCancellationToken);
                await RevokeOtherProofsAsync(
                    state,
                    operationCancellationToken);
                StageNotification(
                    state,
                    notificationKind);

                if (challenge.Purpose != TwoFactorFlowPurpose.SignIn)
                    challenge.ConsumeManagement(Now());

                return new TwoFactorRecoveryCodesResponse { RecoveryCodes = codes };
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TwoFactorStatusResponse> GetStatusAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {

            if (!await context.Users.AnyAsync(
                    member => member.Id == memberId,
                    cancellationToken))
                throw new InvalidAuthenticationSessionException();

            var result = await context.MemberTwoFactors
                .AsNoTracking()
                .Where(factor => factor.MemberId == memberId)
                .Select(factor => new TwoFactorStatusResponse
                {
                    IsEnabled = factor.CredentialId != null,
                    RemainingRecoveryCodes = context.TwoFactorRecoveryCodes.Count(code =>
                        code.MemberId == memberId && code.CredentialId == factor.CredentialId && code.ConsumedAt == null)
                })
                .SingleOrDefaultAsync(cancellationToken);

            return result ?? new TwoFactorStatusResponse();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc/>
    public Task<TwoFactorChallengeResponse> ReauthenticateAsync(
        Guid memberId,
        Guid accessTokenId,
        TwoFactorFlowPurpose purpose,
        string? code,
        string? recoveryCode,
        CancellationToken cancellationToken)
    {
        var flow = cryptography.CreateFlow();

        return RunAsync(
            operationCancellationToken => LoadManagementAsync(
                memberId,
                accessTokenId,
                flow,
                operationCancellationToken),
            async (state, operationCancellationToken) =>
            {
                StartVerification(state.Factor);

                if (recoveryCode is not null)
                {

                    if (purpose != TwoFactorFlowPurpose.ReplaceAuthenticator)
                        throw new TwoFactorOperationConflictException();

                    var reserved = await ReserveRecoveryAsync(
                        state,
                        recoveryCode,
                        operationCancellationToken);
                    state.Challenge.BeginRecovery(
                        reserved.Id,
                        Now());
                    StageNotification(
                        state,
                        AuthenticationEmailKind.TwoFactorRecoveryCodeUsed);
                }
                else
                {
                    VerifyCurrentCode(
                        state.Factor,
                        code);
                }

                var sessionId = await context.AuthenticationAccessTokens
                    .Where(token => token.Id == accessTokenId)
                    .Select(token => token.SessionId)
                    .SingleAsync(operationCancellationToken);
                state.Challenge.AuthorizeManagement(
                    purpose,
                    sessionId,
                    Now());

                return CreateChallengeResponse(
                    flow.Value,
                    state.Challenge);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TwoFactorRecoveryCodesResponse> RegenerateRecoveryCodesAsync(
        string flow,
        CancellationToken cancellationToken)
    {

        return RunFlowAsync(
            flow,
            async (state, operationCancellationToken) =>
            {

                if (state.Challenge.Purpose != TwoFactorFlowPurpose.RegenerateRecoveryCodes ||
                    state.Challenge.RequiredAction != TwoFactorRequiredAction.Complete)
                    throw new TwoFactorOperationConflictException();

                var codes = await ReplaceRecoveryCodesAsync(
                    state,
                    operationCancellationToken);
                await RevokeOtherProofsAsync(
                    state,
                    operationCancellationToken);
                state.Challenge.ConsumeManagement(Now());
                StageNotification(
                    state,
                    AuthenticationEmailKind.TwoFactorRecoveryCodesRegenerated);

                return new TwoFactorRecoveryCodesResponse { RecoveryCodes = codes };
            },
            cancellationToken);
    }

    /// <summary>Stages a credential-free notification atomically with its security operation.</summary>
    /// <param name="state">The current locked account.</param>
    /// <param name="kind">The security event to deliver.</param>
    /// <exception cref="TwoFactorUnavailableException">The account has no usable notification address.</exception>
    private void StageNotification(
        TwoFactorOperationState state,
        AuthenticationEmailKind kind)
    {
        // PostgreSQL requires Email; Identity's inherited property remains nullable.
        var recipient = state.Member.Email!;
        context.AuthenticationEmailOutboxMessages.Add(AuthenticationEmailOutboxMessage.CreateTwoFactorNotification(
            state.Member.Id,
            recipient,
            kind,
            Now()));
    }

    /// <summary>Loads a hashed continuation inside the common transaction boundary.</summary>
    /// <typeparam name="T">The immutable response type.</typeparam>
    /// <param name="flow">The validated opaque proof.</param>
    /// <param name="operation">The operation on account-locked state.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The result after confirmed commit.</returns>
    private Task<T> RunFlowAsync<T>(
        string flow,
        Func<TwoFactorOperationState, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) where T : class
    {

        return RunAsync(
            operationCancellationToken => LoadFlowAsync(
                flow,
                operationCancellationToken),
            operation,
            cancellationToken);
    }

    /// <summary>Commits one operation or failed verification without transparently replaying it.</summary>
    /// <typeparam name="T">The immutable response type.</typeparam>
    /// <param name="load">The account-ordered state loader.</param>
    /// <param name="operation">The operation on the locked state.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The result after the exact operation is confirmed.</returns>
    /// <exception cref="DependencyUnavailableException">The transaction outcome cannot be confirmed.</exception>
    private async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<TwoFactorOperationState>> load,
        Func<TwoFactorOperationState, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) where T : class
    {
        var operationId = Guid.CreateVersion7(timeProvider.GetUtcNow());
        TwoFactorOperationState? state = null;
        T? result = null;
        Exception? rejected = null;
        var commitAttempted = false;
        try
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            state = await load(cancellationToken);
            try
            {
                result = await operation(
                    state,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is TwoFactorAuthenticationFailedException or TwoFactorRateLimitException)
            {
                // Failed attempts are durable even though the HTTP operation is rejected.
                rejected = exception;
            }
            state.Challenge.RecordOperation(operationId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: GoogleSubjectUniqueConstraint or MemberGoogleLoginUniqueConstraint
        })
        {
            // Another association can win after the deferred Google identity was read.
            // Translate only the login constraints; never replay a consumed MFA proof.

            throw new GoogleAccountLinkConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (!commitAttempted || state is null || !await IsOperationConfirmedAsync(
                    state,
                    operationId,
                    result,
                    cancellationToken))
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
        }

        if (rejected is not null)
            ExceptionDispatchInfo.Throw(rejected);

        // Every private operation returns a response; the rejected path above always throws.

        return result!;
    }

    /// <summary>Looks up proof, locks its account, then rechecks its current state.</summary>
    /// <param name="flow">The validated opaque proof.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The locked state bound to this proof.</returns>
    /// <exception cref="TwoFactorAuthenticationFailedException">The proof is unknown or no longer usable.</exception>
    private async Task<TwoFactorOperationState> LoadFlowAsync(
        string flow,
        CancellationToken cancellationToken)
    {
        var hash = cryptography.HashFlow(flow);
        var memberId = await context.TwoFactorChallenges
            .AsNoTracking()
            .Where(challenge => challenge.FlowHash == hash)
            .Select(challenge => (Guid?)challenge.MemberId)
            .SingleOrDefaultAsync(cancellationToken);

        if (memberId is not { } ownerId)
            throw new TwoFactorAuthenticationFailedException();

        var member = await LockMemberAsync(
            ownerId,
            cancellationToken);
        var factor = await context.MemberTwoFactors.SingleOrDefaultAsync(
            candidate => candidate.MemberId == ownerId,
            cancellationToken) ?? throw new TwoFactorUnavailableException();
        var challenge = await context.TwoFactorChallenges.SingleOrDefaultAsync(
            candidate => candidate.FlowHash == hash,
            cancellationToken);

        // The indexed query already requires an exact match of the complete flow hash.

        if (challenge is null || !challenge.IsLive(Now()) ||
            challenge.ExpectedCredentialId != factor.CredentialId ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(member.SecurityStamp ?? string.Empty)),
                challenge.SecurityStampHash))
            throw new TwoFactorAuthenticationFailedException();

        if (challenge.ManagementSessionId is { } sessionId && !await IsSessionActiveAsync(
                sessionId,
                member.Id,
                cancellationToken))
            throw new TwoFactorAuthenticationFailedException();

        if (challenge.Purpose != TwoFactorFlowPurpose.SignIn)
        {
            var caller = callerProvider.GetCurrent();

            if (caller is null || caller.MemberId != member.Id || !await context.AuthenticationAccessTokens.AnyAsync(
                    token => token.Id == caller.AccessTokenId && token.SessionId == challenge.ManagementSessionId,
                    cancellationToken))
                throw new TwoFactorAuthenticationFailedException();
        }

        return new TwoFactorOperationState(
            member,
            factor,
            challenge);
    }

    /// <summary>Locks a fully authenticated member before creating a fresh management proof.</summary>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="accessTokenId">The registered access-token identifier.</param>
    /// <param name="flow">The new opaque grant material.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The locked state and its new, unexposed grant.</returns>
    /// <exception cref="TwoFactorAccessDeniedException">The account has no usable current-factor session.</exception>
    private async Task<TwoFactorOperationState> LoadManagementAsync(
        Guid memberId,
        Guid accessTokenId,
        TwoFactorFlowToken flow,
        CancellationToken cancellationToken)
    {
        var member = await LockMemberAsync(
            memberId,
            cancellationToken);
        var factor = await context.MemberTwoFactors.SingleOrDefaultAsync(
            candidate => candidate.MemberId == memberId,
            cancellationToken);
        var now = Now();
        var sessionId = await context.AuthenticationAccessTokens
            .AsNoTracking()
            .Where(token => token.Id == accessTokenId && token.ExpiresAt >= now.AddSeconds(-AccessTokenConstraints.ClockSkewSeconds))
            .Select(token => (Guid?)token.SessionId)
            .SingleOrDefaultAsync(cancellationToken);

        if (factor?.CredentialId is null || sessionId is not { } authenticatedSessionId ||
            !await context.AuthenticationSessions.AnyAsync(
                session => session.Id == authenticatedSessionId && session.UserId == memberId &&
                    session.RevokedAt == null && session.ExpiresAt > now &&
                    session.TwoFactorCredentialId == factor.CredentialId && session.TwoFactorVerifiedAt != null,
                cancellationToken))
            throw new TwoFactorAccessDeniedException();

        var challenge = TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(timeProvider.GetUtcNow()),
            memberId,
            flow.Hash,
            factor.CredentialId,
            SHA256.HashData(Encoding.UTF8.GetBytes(member.SecurityStamp ?? string.Empty)),
            false,
            null,
            now);
        context.TwoFactorChallenges.Add(challenge);

        return new TwoFactorOperationState(
            member,
            factor,
            challenge);
    }

    /// <summary>Locks the parent account before any factor, challenge or session state.</summary>
    /// <param name="memberId">The expected account.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The locked account.</returns>
    /// <exception cref="TwoFactorAuthenticationFailedException">The account no longer exists.</exception>
    private async Task<MonKadoUser> LockMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return await context.Users
            .FromSqlInterpolated($"SELECT *, xmin FROM public.users WHERE id = {memberId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new TwoFactorAuthenticationFailedException();
    }

    /// <summary>Checks an account-bound session without relaxing revocation or expiration.</summary>
    /// <param name="sessionId">The expected session.</param>
    /// <param name="memberId">The expected account.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>Whether the session remains active.</returns>
    private Task<bool> IsSessionActiveAsync(
        Guid sessionId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var now = Now();

        return context.AuthenticationSessions.AnyAsync(
            session => session.Id == sessionId && session.UserId == memberId && session.RevokedAt == null && session.ExpiresAt > now,
            cancellationToken);
    }

    /// <summary>Applies the shared account-wide verification quota and lockout.</summary>
    /// <param name="factor">The locked security state.</param>
    /// <exception cref="TwoFactorRateLimitException">No verification attempt is currently permitted.</exception>
    private void StartVerification(MemberTwoFactor factor)
    {

        if (!factor.TryStartVerification(Now()))
            throw new TwoFactorRateLimitException();
    }

    /// <summary>Verifies and consumes one current-authenticator time interval.</summary>
    /// <param name="factor">The account-locked credential state.</param>
    /// <param name="code">The optional submitted code.</param>
    /// <exception cref="TwoFactorUnavailableException">The active credential cannot be read.</exception>
    /// <exception cref="TwoFactorAuthenticationFailedException">The code is absent, invalid or reused.</exception>
    private void VerifyCurrentCode(
        MemberTwoFactor factor,
        string? code)
    {
        var credentialId = factor.CredentialId ?? throw new TwoFactorUnavailableException();
        // The credential-consistency constraint requires a secret whenever CredentialId is present.
        var encrypted = factor.ProtectedSecret!;
        var secret = cryptography.UnprotectSecret(
            factor.MemberId,
            credentialId,
            encrypted);
        var timeStep = code is null ? null : cryptography.VerifyCode(
            secret,
            code,
            factor.LastAcceptedTimeStep);

        if (timeStep is not { } accepted)
        {
            factor.RecordFailure(Now());

            throw new TwoFactorAuthenticationFailedException();
        }

        factor.AcceptTimeStep(accepted);
    }

    /// <summary>Reserves one recovery code exclusively until the original flow expires.</summary>
    /// <param name="state">The locked security state.</param>
    /// <param name="recoveryCode">The submitted code.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The reserved hashed code.</returns>
    /// <exception cref="TwoFactorAuthenticationFailedException">The code is unknown, consumed or reserved by another flow.</exception>
    private async Task<TwoFactorRecoveryCode> ReserveRecoveryAsync(
        TwoFactorOperationState state,
        string recoveryCode,
        CancellationToken cancellationToken)
    {
        var hash = cryptography.HashRecoveryCode(recoveryCode);
        var code = await context.TwoFactorRecoveryCodes.SingleOrDefaultAsync(
            candidate => candidate.MemberId == state.Member.Id && candidate.CredentialId == state.Factor.CredentialId && candidate.CodeHash == hash,
            cancellationToken);

        if (code is null || !CryptographicOperations.FixedTimeEquals(
                code.CodeHash,
                hash) || !code.TryReserve(
                state.Challenge.Id,
                state.Challenge.ExpiresAt,
                Now()))
        {
            state.Factor.RecordFailure(Now());

            throw new TwoFactorAuthenticationFailedException();
        }

        return code;
    }

    /// <summary>Replaces the entire recovery set without storing plaintext codes.</summary>
    /// <param name="state">The locked security state.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The new codes, held only for the immediate response.</returns>
    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(
        TwoFactorOperationState state,
        CancellationToken cancellationToken)
    {
        var credentialId = state.Factor.CredentialId ?? throw new TwoFactorUnavailableException();
        var previous = await context.TwoFactorRecoveryCodes
            .Where(code => code.MemberId == state.Member.Id)
            .ToArrayAsync(cancellationToken);
        context.TwoFactorRecoveryCodes.RemoveRange(previous);
        var codes = cryptography.CreateRecoveryCodes();
        context.TwoFactorRecoveryCodes.AddRange(codes.Select(code => TwoFactorRecoveryCode.Create(
            Guid.CreateVersion7(timeProvider.GetUtcNow()),
            state.Member.Id,
            credentialId,
            cryptography.HashRecoveryCode(code))));

        return codes;
    }

    /// <summary>Revokes older sessions and other grants atomically with a credential or recovery-code change.</summary>
    /// <param name="state">The one grant allowed to finish this operation.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A task completed when revocations are staged in the transaction.</returns>
    private Task RevokeOtherProofsAsync(
        TwoFactorOperationState state,
        CancellationToken cancellationToken)
    {

        return revocations.RevokeOtherAsync(
            state.Member.Id,
            state.Challenge.Id,
            Now(),
            cancellationToken);
    }

    /// <summary>Stages a refresh session and its exact registered JWT after all required proofs.</summary>
    /// <param name="state">The verified sign-in state.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The credentials returned only after the outer transaction is committed.</returns>
    private async Task<AccountSessionTokens> CreateSessionAsync(
        TwoFactorOperationState state,
        CancellationToken cancellationToken)
    {
        if (state.Challenge.GoogleFlowId is not null)
            await googleFinalizer.FinalizeAsync(
                state.Member,
                state.Challenge,
                cancellationToken);

        var credentialId = state.Factor.CredentialId ?? throw new TwoFactorUnavailableException();
        var verifiedAt = state.Challenge.VerifiedAt ?? throw new TwoFactorAuthenticationFailedException();
        var sessionId = Guid.CreateVersion7(timeProvider.GetUtcNow());
        var refresh = await refreshSessions.CreateAsync(
            state.Member.Id,
            state.Challenge.IsPersistent,
            sessionId,
            state.Challenge.PreviousSessionId,
            cancellationToken);
        var session = context.AuthenticationSessions.Local.Single(candidate => candidate.Id == sessionId);
        session.BindTwoFactor(
            credentialId,
            verifiedAt);
        var access = accessTokens.Create(state.Member.Id);
        context.AuthenticationAccessTokens.Add(new AuthenticationAccessToken
        {
            Id = access.Id,
            SessionId = sessionId,
            IssuedAt = access.IssuedAt,
            ExpiresAt = access.ExpiresAt
        });
        state.Challenge.ConsumeSignIn(
            sessionId,
            access.Id,
            Now());

        return new AccountSessionTokens(
            access,
            refresh.RefreshToken,
            refresh.RefreshTokenExpiresAt,
            refresh.IsPersistent);
    }

    /// <summary>Verifies an exact commit receipt in a new context without repeating the security operation.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="state">The attempted locked state.</param>
    /// <param name="operationId">The exact operation receipt.</param>
    /// <param name="result">The response held in memory, including any exact issued token identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>Whether the exact committed result can still be confirmed.</returns>
    private async Task<bool> IsOperationConfirmedAsync<T>(
        TwoFactorOperationState state,
        Guid operationId,
        T? result,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var challenge = await database.TwoFactorChallenges
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == state.Challenge.Id && candidate.MemberId == state.Member.Id && candidate.LastOperationId == operationId,
                    cancellationToken);

            if (challenge is null || challenge.InvalidatedAt is not null)
                return false;

            if (!await database.Users.AnyAsync(
                    member => member.Id == state.Member.Id && member.SecurityStamp == state.Member.SecurityStamp,
                    cancellationToken) || !await database.MemberTwoFactors.AnyAsync(
                    factor => factor.MemberId == state.Member.Id && factor.CredentialId == state.Factor.CredentialId,
                    cancellationToken))
                return false;

            if (result is TwoFactorCompletionResult { Tokens: { } tokens })
            {
                var now = Now();
                var refreshHash = SHA256.HashData(Encoding.UTF8.GetBytes(tokens.RefreshToken));
                var currentSessions = AuthenticationSessionQueries.WithValidTwoFactor(database);

                return await database.AuthenticationAccessTokens.AnyAsync(
                    token => token.Id == tokens.AccessToken.Id && token.Id == challenge.ResultAccessTokenId &&
                        token.SessionId == challenge.ResultSessionId && token.IssuedAt == tokens.AccessToken.IssuedAt &&
                        token.ExpiresAt == tokens.AccessToken.ExpiresAt &&
                        token.ExpiresAt >= now.AddSeconds(-AccessTokenConstraints.ClockSkewSeconds) &&
                        currentSessions.Any(session => session.Id == token.SessionId &&
                            session.UserId == state.Member.Id && session.RevokedAt == null && session.ExpiresAt > now &&
                            session.RefreshTokenHash == refreshHash),
                    cancellationToken);
            }

            if (result is TwoFactorRecoveryCodesResponse codes)
            {
                var stored = await database.TwoFactorRecoveryCodes
                    .AsNoTracking()
                    .Where(code => code.MemberId == state.Member.Id && code.CredentialId == state.Factor.CredentialId && code.ConsumedAt == null)
                    .Select(code => code.CodeHash)
                    .ToArrayAsync(cancellationToken);
                var expected = codes.RecoveryCodes
                    .Select(cryptography.HashRecoveryCode)
                    .ToArray();

                return stored.Length == expected.Length && expected.All(hash => stored.Any(candidate => CryptographicOperations.FixedTimeEquals(
                    hash,
                    candidate)));
            }

            if (result is not null && !challenge.IsLive(Now()))
                return false;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            return false;
        }

        return true;
    }

    /// <summary>Projects only the original proof, next action and immutable expiration.</summary>
    /// <param name="flow">The original proof, never a new proof or extended grant.</param>
    /// <param name="challenge">The updated operation state.</param>
    /// <returns>The public challenge response.</returns>
    private static TwoFactorChallengeResponse CreateChallengeResponse(
        string flow,
        TwoFactorChallenge challenge)
    {

        return new TwoFactorChallengeResponse
        {
            Flow = flow,
            RequiredAction = challenge.RequiredAction,
            ExpiresAt = challenge.ExpiresAt
        };
    }

    /// <summary>Reads the injected UTC clock.</summary>
    /// <returns>The current UTC time.</returns>
    private DateTime Now()
    {

        return timeProvider.GetUtcNow().UtcDateTime;
    }
}
