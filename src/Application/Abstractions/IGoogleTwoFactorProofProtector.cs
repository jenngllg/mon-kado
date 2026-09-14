using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Protects deferred Google association authority independently of authenticator secrets.</summary>
public interface IGoogleTwoFactorProofProtector
{
    /// <summary>Encrypts validated first-factor authority for one account and challenge.</summary>
    /// <param name="memberId">The resolved account.</param>
    /// <param name="challengeId">The one-time challenge.</param>
    /// <param name="proof">The server-validated association authority.</param>
    /// <returns>The authenticated ciphertext suitable for PostgreSQL storage.</returns>
    string Protect(
        Guid memberId,
        Guid challengeId,
        GoogleTwoFactorProof proof);

    /// <summary>Authenticates and decrypts only the proof belonging to this account and challenge.</summary>
    /// <param name="memberId">The resolved account.</param>
    /// <param name="challengeId">The one-time challenge.</param>
    /// <param name="protectedProof">The stored ciphertext.</param>
    /// <returns>The server-validated association authority.</returns>
    /// <exception cref="Common.Exceptions.TwoFactorUnavailableException">The proof cannot be authenticated or decrypted.</exception>
    GoogleTwoFactorProof Unprotect(
        Guid memberId,
        Guid challengeId,
        string protectedProof);
}
