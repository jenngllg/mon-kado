using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

using OtpNet;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Protects second-factor material and verifies TOTP without retaining plaintext credentials.</summary>
/// <param name="dataProtection">The shared, persistent application key ring.</param>
/// <param name="timeProvider">The verification clock.</param>
public class TwoFactorCryptography(
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider) : ITwoFactorCryptography
{
    private const string ProtectionPurpose = "MonKado.TwoFactor.Secret.v1";
    private const int RecoveryCodeGroupLength = 8;

    /// <inheritdoc/>
    public string CreateSecret()
    {

        return Base32Encoding.ToString(RandomNumberGenerator.GetBytes(TwoFactorConstraints.SecretByteLength));
    }

    /// <inheritdoc/>
    public string ProtectSecret(
        Guid memberId,
        Guid credentialId,
        string secret)
    {
        try
        {

            return CreateProtector(
                    memberId,
                    credentialId)
                .Protect(secret);
        }
        catch (CryptographicException)
        {

            throw new TwoFactorUnavailableException();
        }
    }

    /// <inheritdoc/>
    public string UnprotectSecret(
        Guid memberId,
        Guid credentialId,
        string protectedSecret)
    {
        try
        {

            return CreateProtector(
                    memberId,
                    credentialId)
                .Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            // A corrupt payload or missing key must not downgrade an account to single-factor authentication.

            throw new TwoFactorUnavailableException();
        }
    }

    /// <inheritdoc/>
    public long? VerifyCode(
        string secret,
        string code,
        long? lastAcceptedTimeStep)
    {
        var verifier = new Totp(
            Base32Encoding.ToBytes(secret),
            step: TwoFactorConstraints.PeriodSeconds,
            mode: OtpHashMode.Sha1,
            totpSize: TwoFactorConstraints.CodeLength);
        var valid = verifier.VerifyTotp(
            timeProvider.GetUtcNow().UtcDateTime,
            code,
            out var matchedTimeStep,
            new VerificationWindow(
                previous: 1,
                future: 1));

        if (!valid || lastAcceptedTimeStep >= matchedTimeStep)
            return null;

        return matchedTimeStep;
    }

    /// <inheritdoc/>
    public string CreateSetupUri(
        string secret,
        string accountLabel)
    {
        var label = Uri.EscapeDataString($"{TwoFactorConstraints.Issuer}:{accountLabel}");
        var encodedSecret = Uri.EscapeDataString(secret);

        return $"otpauth://totp/{label}?secret={encodedSecret}&issuer={TwoFactorConstraints.Issuer}&algorithm=SHA1&digits={TwoFactorConstraints.CodeLength}&period={TwoFactorConstraints.PeriodSeconds}";
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> CreateRecoveryCodes()
    {

        return Enumerable.Range(
                0,
                TwoFactorConstraints.RecoveryCodeCount)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(TwoFactorConstraints.RecoveryCodeByteLength)))
            .Select(code => string.Join(
                "-",
                code
                    .Chunk(RecoveryCodeGroupLength)
                    .Select(group => new string(group))))
            .ToArray();
    }

    /// <inheritdoc/>
    public byte[] HashRecoveryCode(string code)
    {
        var normalized = code
            .Trim()
            .Replace(
                "-",
                string.Empty,
                StringComparison.Ordinal)
            .ToUpperInvariant();

        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }

    /// <inheritdoc/>
    public TwoFactorFlowToken CreateFlow()
    {
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TwoFactorConstraints.FlowByteLength));

        return new TwoFactorFlowToken
        {
            Value = value,
            Hash = HashFlow(value)
        };
    }

    /// <inheritdoc/>
    public byte[] HashFlow(string flow)
    {

        return SHA256.HashData(Encoding.UTF8.GetBytes(flow));
    }

    /// <summary>Binds encryption to both the owner and the exact authenticator version.</summary>
    /// <param name="memberId">The credential owner.</param>
    /// <param name="credentialId">The credential version.</param>
    /// <returns>The purpose-isolated authenticated protector.</returns>
    private IDataProtector CreateProtector(
        Guid memberId,
        Guid credentialId)
    {

        return dataProtection.CreateProtector(
            ProtectionPurpose,
            memberId.ToString("N"),
            credentialId.ToString("N"));
    }
}
