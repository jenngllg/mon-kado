using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates or links member accounts from validated Google identities and establishes MonKado sessions.
/// </summary>
public interface IGoogleAccountSessionService
{
    /// <summary>
    /// Resolves the member currently associated with a validated Google identity.
    /// </summary>
    /// <param name="identity">The validated Google identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The linked or email-matching member identifier, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<Guid?> ResolveExpectedMemberIdAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Completes a validated Google authentication flow automatically when policy allows it.
    /// </summary>
    /// <param name="authenticationContext">The validated identity and protected browser context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The automatic completion outcome.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The identity cannot safely resolve to a member.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<GoogleAuthenticationResult> CompleteAsync(
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken);

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
    Task<GoogleAccountLinkResult> LinkAsync(
        GoogleAuthenticationContext authenticationContext,
        string currentPassword,
        CancellationToken cancellationToken);
}
