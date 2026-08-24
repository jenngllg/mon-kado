using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Resolves validated Google identities to MonKado members and creates their sessions.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="userRepository">The MonKado user repository.</param>
/// <param name="googleAccountRepository">The Google account repository.</param>
/// <param name="memberRepository">The member repository.</param>
/// <param name="emailChangeRequestRepository">The member email change request repository.</param>
/// <param name="outboxRepository">The authentication email outbox repository.</param>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="refreshSessionService">The refresh session service.</param>
/// <param name="refreshTokenService">The refresh token service.</param>
/// <param name="userManager">The Identity user manager.</param>
/// <param name="passwordHasher">The password hasher used to equalize non-authoritative link failures.</param>
/// <param name="lookupNormalizer">The Identity lookup normalizer.</param>
/// <param name="accessTokenService">The access token service.</param>
/// <param name="logger">The logger.</param>
/// <param name="timeProvider">The time provider.</param>
public class GoogleAccountSessionService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IGoogleAccountRepository googleAccountRepository,
    IMemberRepository memberRepository,
    IMemberEmailChangeRequestRepository emailChangeRequestRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    IAuthenticationSessionRepository sessionRepository,
    IRefreshSessionService refreshSessionService,
    IRefreshTokenService refreshTokenService,
    UserManager<MonKadoUser> userManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    ILookupNormalizer lookupNormalizer,
    IAccessTokenService accessTokenService,
    ILogger<GoogleAccountSessionService> logger,
    TimeProvider timeProvider) : IGoogleAccountSessionService
{
    private const string DefaultDisplayName = "Membre";
    private const int MaximumTransactionRetryCount = 3;
    private const int SecurityStampByteLength = 20;
    private static readonly TimeSpan _maximumTransactionRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Resolves the member currently associated with a validated Google identity.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The linked or email-matching member identifier, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async Task<Guid?> ResolveExpectedMemberIdAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var executionStrategy = CreateTransactionExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
            {
                var linkedMemberId = await googleAccountRepository.GetMemberIdBySubjectAsync(
                    GetRequiredSubject(identity),
                    cancellationToken);

                if (linkedMemberId is not null)
                    return linkedMemberId;

                var normalizedEmail = NormalizeEmail(GetRequiredEmail(identity));

                return await userRepository.Query()
                    .Where(user => user.NormalizedEmail == normalizedEmail)
                    .Select(user => (Guid?)user.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            });
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Completes a validated Google authentication flow automatically when policy allows it.
    /// </summary>
    /// <param name="authenticationContext">The validated identity and protected browser context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The automatic completion outcome.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The identity cannot safely resolve to a member.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async Task<GoogleAuthenticationResult> CompleteAsync(
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await CompleteWithConcurrencyRetryAsync(
                authenticationContext,
                cancellationToken);
            LogSuccessfulCompletion(result);

            return result;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Retries one completion after a concurrent identity uniqueness conflict.
    /// </summary>
    /// <param name="authenticationContext">The protected Google authentication context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Google authentication result.</returns>
    /// <exception cref="GoogleAuthenticationFailedException">The flow was replayed or cannot be resolved safely.</exception>
    private async Task<GoogleAuthenticationResult> CompleteWithConcurrencyRetryAsync(
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        try
        {

            return await ExecuteCompletionAsync(
                authenticationContext,
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAuthenticationFlowReplay(exception))
        {

            throw new GoogleAuthenticationFailedException();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            context.ChangeTracker.Clear();

            try
            {

                return await ExecuteCompletionAsync(
                    authenticationContext,
                    cancellationToken);
            }
            catch (DbUpdateException retryException) when (IsUniqueViolation(retryException))
            {

                throw new GoogleAuthenticationFailedException();
            }
        }
    }

    /// <summary>
    /// Links a validated Google identity after verifying the current MonKado password.
    /// </summary>
    /// <param name="authenticationContext">The validated identity and protected browser context.</param>
    /// <param name="currentPassword">The exact current MonKado password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The explicit link outcome and its session tokens when successful.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The flow was already consumed or no longer resolves safely.</exception>
    /// <exception cref="InvalidOperationException">An Identity persistence mutation fails unexpectedly.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async Task<GoogleAccountLinkResult> LinkAsync(
        GoogleAuthenticationContext authenticationContext,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var executionStrategy = CreateTransactionExecutionStrategy();
            var executionState = new GoogleAuthenticationExecutionState(authenticationContext);

            var result = await executionStrategy.ExecuteInTransactionAsync(
                executionState,
                (
                    state,
                    operationCancellationToken) => ExecuteLinkAttemptAsync(
                        state,
                        currentPassword,
                        operationCancellationToken),
                WasAuthenticationOperationCommittedAsync,
                cancellationToken);
            LogSuccessfulLink(result);

            return result;
        }
        catch (DbUpdateException exception) when (IsAuthenticationFlowReplay(exception))
        {

            throw new GoogleAuthenticationFailedException();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {

            return new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.Conflict,
                null);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Executes one completion attempt in a retriable transaction.
    /// </summary>
    /// <param name="authenticationContext">The protected Google authentication context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Google authentication result.</returns>
    private async Task<GoogleAuthenticationResult> ExecuteCompletionAsync(
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        var executionStrategy = CreateTransactionExecutionStrategy();
        var executionState = new GoogleAuthenticationExecutionState(authenticationContext);

        return await executionStrategy.ExecuteInTransactionAsync(
            executionState,
            ExecuteCompletionAttemptAsync,
            WasAuthenticationOperationCommittedAsync,
            cancellationToken);
    }

    /// <summary>
    /// Clears prior attempt markers and completes one transactional authentication attempt.
    /// </summary>
    /// <param name="executionState">The transaction execution state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Google authentication result.</returns>
    private async Task<GoogleAuthenticationResult> ExecuteCompletionAttemptAsync(
        GoogleAuthenticationExecutionState executionState,
        CancellationToken cancellationToken)
    {
        executionState.Reset();
        var result = await CompleteOnceAsync(
            executionState.AuthenticationContext,
            cancellationToken);

        if (result.MemberId is { } memberId &&
            result.Session is { } session)
            executionState.RecordSession(
                memberId,
                session.RefreshToken);

        return result;
    }

    /// <summary>
    /// Resolves the Google identity and stages one automatic completion result.
    /// </summary>
    /// <param name="authenticationContext">The protected Google authentication context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The staged Google authentication result.</returns>
    private async Task<GoogleAuthenticationResult> CompleteOnceAsync(
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var identity = authenticationContext.Identity;
        var subject = GetRequiredSubject(identity);
        var linkedMemberId = await googleAccountRepository.GetMemberIdBySubjectAsync(
            subject,
            cancellationToken);

        if (linkedMemberId is { } memberId)
        {
            EnsureExpectedMember(
                authenticationContext.ExpectedMemberId,
                memberId,
                subjectIsLinked: true);
            var linkedResult = await CompleteLinkedMemberAsync(
                memberId,
                authenticationContext,
                cancellationToken);

            return linkedResult;
        }

        var email = GetRequiredEmail(identity);
        var normalizedEmail = NormalizeEmail(email);
        var user = await userRepository.GetByNormalizedEmailForUpdateAsync(
            normalizedEmail,
            cancellationToken);
        var existingSubject = user is null
            ? null
            : await googleAccountRepository.GetSubjectByMemberIdAsync(
                user.Id,
                cancellationToken);
        EnsureExpectedMember(
            authenticationContext.ExpectedMemberId,
            user?.Id,
            string.Equals(
                existingSubject,
                subject,
                StringComparison.Ordinal));
        var googleCanCreateAccount = CanCreateAccountWithoutAdditionalVerification(identity);

        if (!googleCanCreateAccount)
            return new GoogleAuthenticationResult(
                GoogleAuthenticationOutcome.AdditionalVerificationRequired,
                null);

        if (user is null)
            return await CreateGoogleMemberAsync(
                email,
                normalizedEmail,
                subject,
                authenticationContext,
                cancellationToken);

        if (existingSubject is not null &&
            !string.Equals(
                existingSubject,
                subject,
                StringComparison.Ordinal))
            throw new GoogleAuthenticationFailedException();

        if (existingSubject is not null)
            return await CompleteLinkedMemberAsync(
                user.Id,
                authenticationContext,
                cancellationToken);

        if (!CanAutoLinkExistingAccount(
            user,
            identity))
            return new GoogleAuthenticationResult(
                GoogleAuthenticationOutcome.ExplicitLinkRequired,
                null);

        return await LinkAuthoritativeMemberAsync(
            user,
            subject,
            authenticationContext,
            cancellationToken);
    }

    /// <summary>
    /// Completes authentication for a member already linked to the Google subject.
    /// </summary>
    /// <param name="memberId">The linked member identifier.</param>
    /// <param name="authenticationContext">The protected Google authentication context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created refresh session result.</returns>
    private async Task<GoogleAuthenticationResult> CompleteLinkedMemberAsync(
        Guid memberId,
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdForUpdateAsync(
            memberId,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (user is null)
            throw new GoogleAuthenticationFailedException();

        var canConfirmEmail = !user.EmailConfirmed &&
            CanConfirmLocalEmail(
                user,
                authenticationContext.Identity);

        if (!canConfirmEmail &&
            IsLockedOut(
                user,
                now))
            throw new GoogleAuthenticationFailedException();

        if (canConfirmEmail)
            await SecureAuthoritativeAccountClaimAsync(
                user,
                authenticationContext.Identity,
                replaceDisplayName: true,
                now,
                cancellationToken);

        ResetPasswordFailures(user);
        var refreshSession = await refreshSessionService.CreateAsync(
            user.Id,
            authenticationContext.IsPersistent,
            authenticationContext.FlowId,
            authenticationContext.CurrentSessionId,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GoogleAuthenticationResult(
            GoogleAuthenticationOutcome.SessionCreated,
            refreshSession,
            user.Id,
            GoogleMemberResolution.Found);
    }

    /// <summary>
    /// Invalidates credentials and sessions inherited from a local registration before trusting Google.
    /// </summary>
    /// <param name="user">The member being confirmed.</param>
    /// <param name="identity">The authoritative Google identity.</param>
    /// <param name="replaceDisplayName">Whether to replace the untrusted local display name.</param>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task SecureAuthoritativeAccountClaimAsync(
        MonKadoUser user,
        GoogleIdentity identity,
        bool replaceDisplayName,
        DateTime now,
        CancellationToken cancellationToken)
    {
        user.PasswordHash = null;
        user.SecurityStamp = CreateSecurityStamp();
        var emailChangeRequest = await emailChangeRequestRepository
            .GetActiveByUserIdForUpdateAsync(
                user.Id,
                cancellationToken);

        if (emailChangeRequest is not null)
        {
            emailChangeRequest.Revoke(now);
            await outboxRepository.MarkPendingEmailChangeMessagesProcessedAsync(
                emailChangeRequest.Id,
                now,
                cancellationToken);
        }

        _ = await sessionRepository.RevokeAllForUserAsync(
            user.Id,
            now,
            cancellationToken);

        if (replaceDisplayName)
            user.DisplayName = identity.DisplayName is null
                ? DefaultDisplayName
                : identity.DisplayName.Trim();

        ConfirmEmail(user);
        await outboxRepository.MarkPendingConfirmationMessagesProcessedAsync(
            user.Id,
            now,
            cancellationToken);
    }

    /// <summary>
    /// Creates a passwordless member, Google login, Member role, and refresh session.
    /// </summary>
    /// <param name="email">The validated Google email address.</param>
    /// <param name="normalizedEmail">The normalized email address.</param>
    /// <param name="subject">The validated Google subject.</param>
    /// <param name="authenticationContext">The protected Google authentication context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created refresh session result.</returns>
    private async Task<GoogleAuthenticationResult> CreateGoogleMemberAsync(
        string email,
        string normalizedEmail,
        string subject,
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = CreateGoogleMember(
            email,
            normalizedEmail,
            authenticationContext.Identity.DisplayName,
            now);
        userRepository.Add(user);
        memberRepository.AddMemberRole(user.Id);
        googleAccountRepository.AddLogin(
            user.Id,
            subject);
        var refreshSession = await refreshSessionService.CreateAsync(
            user.Id,
            authenticationContext.IsPersistent,
            authenticationContext.FlowId,
            authenticationContext.CurrentSessionId,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GoogleAuthenticationResult(
            GoogleAuthenticationOutcome.SessionCreated,
            refreshSession,
            user.Id,
            GoogleMemberResolution.Created);
    }

    /// <summary>
    /// Automatically links an authoritative identity to an unconfirmed member, or Gmail to a confirmed member.
    /// </summary>
    /// <param name="user">The existing member.</param>
    /// <param name="subject">The validated Google subject.</param>
    /// <param name="authenticationContext">The protected Google authentication context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created refresh session result.</returns>
    private async Task<GoogleAuthenticationResult> LinkAuthoritativeMemberAsync(
        MonKadoUser user,
        string subject,
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var replaceDisplayName = !user.EmailConfirmed;
        await SecureAuthoritativeAccountClaimAsync(
            user,
            authenticationContext.Identity,
            replaceDisplayName,
            now,
            cancellationToken);

        ResetPasswordFailures(user);
        googleAccountRepository.AddLogin(
            user.Id,
            subject);
        var refreshSession = await refreshSessionService.CreateAsync(
            user.Id,
            authenticationContext.IsPersistent,
            authenticationContext.FlowId,
            authenticationContext.CurrentSessionId,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GoogleAuthenticationResult(
            GoogleAuthenticationOutcome.SessionCreated,
            refreshSession,
            user.Id,
            GoogleMemberResolution.Linked);
    }

    /// <summary>
    /// Clears prior attempt markers and stages one explicit-link transaction attempt.
    /// </summary>
    /// <param name="executionState">The transaction execution state.</param>
    /// <param name="currentPassword">The current MonKado password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The staged explicit-link result.</returns>
    private async Task<GoogleAccountLinkResult> ExecuteLinkAttemptAsync(
        GoogleAuthenticationExecutionState executionState,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        executionState.Reset();
        var result = await LinkOnceAsync(
            executionState,
            currentPassword,
            cancellationToken);

        if (result.MemberId is { } memberId &&
            result.Tokens is { } tokens)
            executionState.RecordSession(
                memberId,
                tokens.RefreshToken);

        return result;
    }

    /// <summary>
    /// Verifies the password and stages one explicit Google account link.
    /// </summary>
    /// <param name="executionState">The transaction execution state.</param>
    /// <param name="currentPassword">The current MonKado password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The staged explicit-link result.</returns>
    private async Task<GoogleAccountLinkResult> LinkOnceAsync(
        GoogleAuthenticationExecutionState executionState,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var authenticationContext = executionState.AuthenticationContext;
        var identity = authenticationContext.Identity;
        var email = GetRequiredEmail(identity);
        var normalizedEmail = NormalizeEmail(email);
        var user = await userRepository.GetByNormalizedEmailForUpdateAsync(
            normalizedEmail,
            cancellationToken);
        var subject = GetRequiredSubject(identity);
        var existingSubject = user is null
            ? null
            : await googleAccountRepository.GetSubjectByMemberIdAsync(
                user.Id,
                cancellationToken);

        EnsureExpectedMember(
            authenticationContext.ExpectedMemberId,
            user?.Id,
            string.Equals(
                existingSubject,
                subject,
                StringComparison.Ordinal));
        var flowWasConsumed = await sessionRepository.GetByIdAsync(
            authenticationContext.FlowId,
            cancellationToken) is not null;

        if (flowWasConsumed)
            throw new GoogleAuthenticationFailedException();

        if (user is null ||
            !user.EmailConfirmed)
        {
            PerformPasswordTimingEqualization(currentPassword);

            return new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.InvalidCredentials,
                null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (IsLockedOut(
            user,
            now))
        {
            PerformPasswordTimingEqualization(currentPassword);

            return new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.InvalidCredentials,
                null);
        }

        if (user.PasswordHash is null)
        {
            PerformPasswordTimingEqualization(currentPassword);

            return new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.InvalidCredentials,
                null);
        }

        var passwordIsValid = await userManager.CheckPasswordAsync(
            user,
            currentPassword);

        if (!passwordIsValid)
        {
            var result = await userManager.AccessFailedAsync(user);
            EnsureIdentityUpdateSucceeded(
                result,
                "record the failed Google account link attempt");
            executionState.RecordPasswordFailure();

            return new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.InvalidCredentials,
                null);
        }

        var linkedMemberId = await googleAccountRepository.GetMemberIdBySubjectAsync(
            subject,
            cancellationToken);
        var subjectBelongsToAnotherMember = linkedMemberId is { } existingMemberId &&
            existingMemberId != user.Id;
        var memberHasAnotherSubject = existingSubject is not null &&
            !string.Equals(
                existingSubject,
                subject,
                StringComparison.Ordinal);

        if (subjectBelongsToAnotherMember ||
            memberHasAnotherSubject)
            return new GoogleAccountLinkResult(
                GoogleAccountLinkOutcome.Conflict,
                null);

        ResetPasswordFailures(user);

        if (existingSubject is null)
            googleAccountRepository.AddLogin(
                user.Id,
                subject);

        var refreshSession = await refreshSessionService.CreateAsync(
            user.Id,
            authenticationContext.IsPersistent,
            authenticationContext.FlowId,
            authenticationContext.CurrentSessionId,
            cancellationToken);
        var tokens = new AccountSessionTokens(
            accessTokenService.Create(user.Id),
            refreshSession.RefreshToken,
            refreshSession.RefreshTokenExpiresAt,
            refreshSession.IsPersistent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GoogleAccountLinkResult(
            GoogleAccountLinkOutcome.Success,
            tokens,
            user.Id);
    }

    /// <summary>
    /// Verifies the exact refresh secret or terminates an ambiguous failed-password attempt without replaying it.
    /// </summary>
    /// <param name="executionState">The transaction execution state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the exact session was committed or a failed-password attempt must not be replayed.</returns>
    private async Task<bool> WasAuthenticationOperationCommittedAsync(
        GoogleAuthenticationExecutionState executionState,
        CancellationToken cancellationToken)
    {

        if (executionState.AttemptedSessionMemberId is { } memberId &&
            executionState.AttemptedRefreshToken is { } refreshToken)
        {
            var session = await context.AuthenticationSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    storedSession =>
                        storedSession.Id == executionState.AuthenticationContext.FlowId &&
                        storedSession.UserId == memberId,
                    cancellationToken);

            return session is not null &&
                refreshTokenService.Verify(
                    refreshToken,
                    session.RefreshTokenHash);
        }

        return executionState.PasswordFailureWasRecorded;
    }

    /// <summary>
    /// Creates a confirmed passwordless Identity user from validated Google attributes.
    /// </summary>
    /// <param name="email">The validated email address.</param>
    /// <param name="normalizedEmail">The normalized email address.</param>
    /// <param name="displayName">The optional Google display name.</param>
    /// <param name="now">The current UTC date and time.</param>
    /// <returns>The new Identity user.</returns>
    private MonKadoUser CreateGoogleMember(
        string email,
        string normalizedEmail,
        string? displayName,
        DateTime now)
    {
        var normalizedUserName = lookupNormalizer.NormalizeName(email)!;

        return new MonKadoUser
        {
            Id = Guid.CreateVersion7(now),
            Email = email,
            NormalizedEmail = normalizedEmail,
            UserName = email,
            NormalizedUserName = normalizedUserName,
            DisplayName = displayName is null
                ? DefaultDisplayName
                : displayName.Trim(),
            EmailConfirmed = true,
            UnconfirmedAccountExpiresAt = null,
            LockoutEnabled = true,
            SecurityStamp = CreateSecurityStamp(),
            ConcurrencyStamp = Guid.CreateVersion7(now).ToString("D")
        };
    }

    /// <summary>
    /// Creates a cryptographically random Identity security stamp.
    /// </summary>
    /// <returns>A Base64URL-encoded 160-bit security stamp.</returns>
    private static string CreateSecurityStamp()
    {

        return WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(SecurityStampByteLength));
    }

    /// <summary>
    /// Normalizes a Google email already accepted by the FluentValidation pipeline.
    /// </summary>
    /// <param name="email">The validated email address.</param>
    /// <returns>The normalized email address.</returns>
    private string NormalizeEmail(string email)
    {

        return lookupNormalizer.NormalizeEmail(email)!;
    }

    /// <summary>
    /// Gets the Google subject guaranteed by the FluentValidation pipeline.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns>The Google subject.</returns>
    private static string GetRequiredSubject(GoogleIdentity identity)
    {

        return identity.Subject!;
    }

    /// <summary>
    /// Gets and trims the Google email guaranteed by the FluentValidation pipeline.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns>The Google email address.</returns>
    private static string GetRequiredEmail(GoogleIdentity identity)
    {

        return identity.Email!.Trim();
    }

    /// <summary>
    /// Determines whether Google can establish a new account without another verification channel.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns><see langword="true" /> for Gmail and Workspace identities.</returns>
    private static bool CanCreateAccountWithoutAdditionalVerification(GoogleIdentity identity)
    {

        return IsGmailIdentity(identity) || IsWorkspaceIdentity(identity);
    }

    /// <summary>
    /// Determines whether the identity can safely claim an existing account without a local credential.
    /// </summary>
    /// <param name="user">The existing member.</param>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns><see langword="true" /> for Gmail, or Workspace when the local account was never confirmed.</returns>
    private static bool CanAutoLinkExistingAccount(
        MonKadoUser user,
        GoogleIdentity identity)
    {

        return IsGmailIdentity(identity) ||
            !user.EmailConfirmed &&
            IsWorkspaceIdentity(identity);
    }

    /// <summary>
    /// Determines whether Google authoritatively proved the exact unconfirmed local email address.
    /// </summary>
    /// <param name="user">The linked Identity user.</param>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns><see langword="true" /> when Google is authoritative and both normalized emails match.</returns>
    private bool CanConfirmLocalEmail(
        MonKadoUser user,
        GoogleIdentity identity)
    {

        if (!CanCreateAccountWithoutAdditionalVerification(identity))
            return false;

        var googleNormalizedEmail = NormalizeEmail(GetRequiredEmail(identity));

        return string.Equals(
            user.NormalizedEmail,
            googleNormalizedEmail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the validated identity uses the Gmail consumer domain.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns><see langword="true" /> for a Gmail address.</returns>
    private static bool IsGmailIdentity(GoogleIdentity identity)
    {
        var email = GetRequiredEmail(identity);
        var separatorIndex = email.LastIndexOf('@');
        var domain = email[(separatorIndex + 1)..];

        return string.Equals(
            domain,
            "gmail.com",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether Google asserted a hosted Workspace domain.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <returns><see langword="true" /> for a Workspace identity.</returns>
    private static bool IsWorkspaceIdentity(GoogleIdentity identity)
    {

        return !string.IsNullOrWhiteSpace(identity.HostedDomain);
    }

    /// <summary>
    /// Determines whether an Identity user is currently locked out.
    /// </summary>
    /// <param name="user">The Identity user.</param>
    /// <param name="now">The current UTC date and time.</param>
    /// <returns><see langword="true" /> when the lockout is active.</returns>
    private static bool IsLockedOut(
        MonKadoUser user,
        DateTime now)
    {

        return user.LockoutEnabled &&
            user.LockoutEnd is { } lockoutEnd &&
            lockoutEnd.UtcDateTime > now;
    }

    /// <summary>
    /// Marks a Google-authoritative email as confirmed.
    /// </summary>
    /// <param name="user">The Identity user.</param>
    private static void ConfirmEmail(MonKadoUser user)
    {
        user.EmailConfirmed = true;
        user.UnconfirmedAccountExpiresAt = null;
    }

    /// <summary>
    /// Clears prior password failures after successful Google authentication.
    /// </summary>
    /// <param name="user">The Identity user.</param>
    private static void ResetPasswordFailures(MonKadoUser user)
    {
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
    }

    /// <summary>
    /// Performs a dummy password hash so generic non-authoritative failures do not expose account state by timing.
    /// </summary>
    /// <param name="password">The submitted password.</param>
    private void PerformPasswordTimingEqualization(string password)
    {
        var dummyUser = new MonKadoUser
        {
            Id = Guid.Empty,
            UserName = "google-link-timing-equalization"
        };

        _ = passwordHasher.HashPassword(
            dummyUser,
            password);
    }

    /// <summary>
    /// Ensures that an Identity persistence mutation succeeded.
    /// </summary>
    /// <param name="result">The Identity result.</param>
    /// <param name="operation">The operation description used by the technical exception.</param>
    /// <exception cref="InvalidOperationException">The Identity mutation failed.</exception>
    private static void EnsureIdentityUpdateSucceeded(
        IdentityResult result,
        string operation)
    {

        if (result.Succeeded)
            return;

        var errorCodes = string.Join(
            ", ",
            result.Errors.Select(error => error.Code));

        throw new InvalidOperationException($"Unable to {operation}: {errorCodes}.");
    }

    /// <summary>
    /// Enforces the member snapshot captured by the OIDC callback.
    /// A null snapshot may converge only on a member already linked to the exact Google subject.
    /// </summary>
    /// <param name="expectedMemberId">The member identifier resolved at callback time, or <see langword="null" /> when none existed.</param>
    /// <param name="currentMemberId">The member identifier resolved during completion.</param>
    /// <param name="subjectIsLinked">Whether the current member is already linked to the exact validated Google subject.</param>
    /// <exception cref="GoogleAuthenticationFailedException">The protected snapshot no longer matches.</exception>
    private static void EnsureExpectedMember(
        Guid? expectedMemberId,
        Guid? currentMemberId,
        bool subjectIsLinked)
    {

        if (expectedMemberId is { } expectedId &&
            currentMemberId == expectedId)
            return;

        if (expectedMemberId is null &&
            currentMemberId is null)
            return;

        if (expectedMemberId is null &&
            subjectIsLinked)
            return;

        throw new GoogleAuthenticationFailedException();
    }

    /// <summary>
    /// Determines whether an EF update failed on a PostgreSQL unique constraint.
    /// </summary>
    /// <param name="exception">The EF update exception.</param>
    /// <returns><see langword="true" /> for a PostgreSQL unique violation.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {

        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };
    }

    /// <summary>
    /// Determines whether an EF update attempted to reuse an authentication FlowId.
    /// </summary>
    /// <param name="exception">The EF update exception.</param>
    /// <returns><see langword="true" /> when the authentication-session primary key was reused.</returns>
    private static bool IsAuthenticationFlowReplay(DbUpdateException exception)
    {

        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_authentication_sessions"
        };
    }

    /// <summary>
    /// Creates the retry strategy scoped exclusively to idempotent Google authentication transactions.
    /// </summary>
    /// <returns>The PostgreSQL retrying execution strategy.</returns>
    private NpgsqlRetryingExecutionStrategy CreateTransactionExecutionStrategy()
    {

        return new NpgsqlRetryingExecutionStrategy(
            context,
            MaximumTransactionRetryCount,
            _maximumTransactionRetryDelay,
            errorCodesToAdd: null);
    }

    /// <summary>
    /// Logs a successful automatic completion after its transaction was committed or verified.
    /// </summary>
    /// <param name="result">The committed Google authentication result.</param>
    private void LogSuccessfulCompletion(GoogleAuthenticationResult result)
    {

        if (result.MemberResolution is not { } memberResolution)
            return;

        var memberId = result.MemberId.GetValueOrDefault();

        switch (memberResolution)
        {
            case GoogleMemberResolution.Created:
                ApplicationLogMessages.GoogleMemberCreated(
                    logger,
                    memberId);
                break;
            case GoogleMemberResolution.Found:
                ApplicationLogMessages.GoogleMemberFound(
                    logger,
                    memberId);
                break;
            default:
                ApplicationLogMessages.GoogleAccountLinked(
                    logger,
                    memberId);
                break;
        }

        ApplicationLogMessages.GoogleSessionCreated(
            logger,
            memberId);
    }

    /// <summary>
    /// Logs a successful explicit link after its transaction was committed or verified.
    /// </summary>
    /// <param name="result">The committed Google account-link result.</param>
    private void LogSuccessfulLink(GoogleAccountLinkResult result)
    {

        if (result.Outcome != GoogleAccountLinkOutcome.Success ||
            result.MemberId is not { } memberId)
            return;

        ApplicationLogMessages.GoogleAccountLinked(
            logger,
            memberId);
        ApplicationLogMessages.GoogleSessionCreated(
            logger,
            memberId);
    }
}
