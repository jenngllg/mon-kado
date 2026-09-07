using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Resolves the protected Google identity bound to the current browser request.</summary>
public interface IGoogleAuthenticationContextProvider
{
    /// <summary>Verifies the submitted binding and reads its protected authentication context.</summary>
    /// <param name="flow">The validated browser-flow binding.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The protected Google authentication context.</returns>
    Task<GoogleAuthenticationContext> GetAsync(
        string flow,
        CancellationToken cancellationToken);
}
