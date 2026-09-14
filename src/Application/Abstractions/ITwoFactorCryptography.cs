using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Provides TOTP verification and cryptographic second-factor credentials without persistence.</summary>
public interface ITwoFactorCryptography
{
    /// <summary>Creates a fresh Base32 authenticator secret.</summary>
    /// <returns>A cryptographically random 160-bit secret.</returns>
    string CreateSecret();

    /// <summary>Protects a shared secret for one member and credential version.</summary>
    /// <param name="memberId">The credential owner.</param>
    /// <param name="credentialId">The credential version.</param>
    /// <param name="secret">The Base32 secret.</param>
    /// <returns>The authenticated encrypted secret.</returns>
    string ProtectSecret(
        Guid memberId,
        Guid credentialId,
        string secret);

    /// <summary>Unprotects a shared secret bound to its owner and version.</summary>
    /// <param name="memberId">The credential owner.</param>
    /// <param name="credentialId">The credential version.</param>
    /// <param name="protectedSecret">The protected secret.</param>
    /// <returns>The decrypted Base32 secret.</returns>
    /// <exception cref="Common.Exceptions.TwoFactorUnavailableException">The secret cannot be authenticated or decrypted.</exception>
    string UnprotectSecret(
        Guid memberId,
        Guid credentialId,
        string protectedSecret);

    /// <summary>Verifies a TOTP against the injected clock and the last consumed interval.</summary>
    /// <param name="secret">The Base32 shared secret.</param>
    /// <param name="code">The six-digit code already validated by the application pipeline.</param>
    /// <param name="lastAcceptedTimeStep">The last transactionally consumed interval.</param>
    /// <returns>The matching unused interval, or null when verification fails.</returns>
    long? VerifyCode(
        string secret,
        string code,
        long? lastAcceptedTimeStep);

    /// <summary>Creates the sensitive authenticator enrollment URI.</summary>
    /// <param name="secret">The Base32 shared secret.</param>
    /// <param name="accountLabel">The account label displayed by the authenticator.</param>
    /// <returns>An otpauth URI to be displayed only during enrollment.</returns>
    string CreateSetupUri(
        string secret,
        string accountLabel);

    /// <summary>Creates ten independent high-entropy recovery codes.</summary>
    /// <returns>The codes to display once to the account owner.</returns>
    IReadOnlyList<string> CreateRecoveryCodes();

    /// <summary>Hashes a validated recovery code after removing its display formatting.</summary>
    /// <param name="code">The validated hexadecimal code, optionally grouped by hyphens.</param>
    /// <returns>A SHA-256 hash suitable for durable storage.</returns>
    byte[] HashRecoveryCode(string code);

    /// <summary>Creates a proof whose plaintext is returned only to the browser.</summary>
    /// <returns>The transient proof and its durable hash.</returns>
    TwoFactorFlowToken CreateFlow();

    /// <summary>Hashes an already validated canonical challenge proof.</summary>
    /// <param name="flow">The unpadded Base64URL proof.</param>
    /// <returns>The proof's SHA-256 hash.</returns>
    byte[] HashFlow(string flow);
}
