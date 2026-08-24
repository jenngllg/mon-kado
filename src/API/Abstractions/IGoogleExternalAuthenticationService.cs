using JennGllg.Fr.MonKado.Back.Api.Models;

using Microsoft.AspNetCore.Authentication;

namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Creates and consumes the protected short-lived Google authentication state.
/// </summary>
public interface IGoogleExternalAuthenticationService
{
    /// <summary>
    /// Creates protected properties for a Google OpenID Connect challenge.
    /// </summary>
    /// <param name="returnPath">The validated frontend return path.</param>
    /// <param name="rememberMe">Whether the resulting MonKado session is persistent.</param>
    /// <param name="currentSessionId">The optional prior session proven by the browser.</param>
    /// <returns>The challenge properties.</returns>
    AuthenticationProperties CreateChallengeProperties(
        string returnPath,
        bool rememberMe,
        Guid? currentSessionId);

    /// <summary>
    /// Creates a callback-specific opaque browser-flow binding.
    /// </summary>
    /// <returns>A cryptographically random 256-bit Base64Url binding.</returns>
    string CreateFlowBinding();

    /// <summary>
    /// Authenticates and reconstructs the minimal protected Google context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The protected context, or <see langword="null" /> when it is invalid.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<GoogleExternalAuthenticationTicket?> AuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads and validates the opaque browser-flow binding from protected properties.
    /// </summary>
    /// <param name="properties">The protected authentication properties.</param>
    /// <param name="flowBinding">The opaque flow binding when valid.</param>
    /// <returns><see langword="true" /> when the binding is present and valid.</returns>
    bool TryGetFlowBinding(
        AuthenticationProperties properties,
        out string flowBinding);

    /// <summary>
    /// Compares a protected flow binding with the value returned by the browser.
    /// </summary>
    /// <param name="protectedFlowBinding">The binding stored in the protected ticket.</param>
    /// <param name="browserFlowBinding">The binding returned by the browser.</param>
    /// <returns><see langword="true" /> when both bindings match.</returns>
    bool MatchesFlowBinding(
        string protectedFlowBinding,
        string? browserFlowBinding);

    /// <summary>
    /// Adds an opaque browser-flow binding to a relative path.
    /// </summary>
    /// <param name="path">The relative API or frontend path.</param>
    /// <param name="flowBinding">The validated opaque flow binding.</param>
    /// <returns>The bound relative path.</returns>
    string BuildBoundPath(
        string path,
        string flowBinding);

    /// <summary>
    /// Deletes the short-lived external identity cookie.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task DeleteAsync(
        HttpContext context,
        CancellationToken cancellationToken);
}
