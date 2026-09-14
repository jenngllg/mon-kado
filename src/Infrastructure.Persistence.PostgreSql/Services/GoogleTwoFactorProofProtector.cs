using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.DataProtection;

using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Encrypts deferred Google proof with a purpose distinct from authenticator-secret storage.</summary>
/// <param name="dataProtection">The persistent shared key ring.</param>
public class GoogleTwoFactorProofProtector(IDataProtectionProvider dataProtection) : IGoogleTwoFactorProofProtector
{
    private const string Purpose = "MonKado.TwoFactor.GoogleProof.v1";

    /// <inheritdoc/>
    public string Protect(
        Guid memberId,
        Guid challengeId,
        GoogleTwoFactorProof proof)
    {
        try
        {

            return CreateProtector(
                    memberId,
                    challengeId)
                .Protect(JsonSerializer.Serialize(proof));
        }
        catch (CryptographicException)
        {

            throw new TwoFactorUnavailableException();
        }
    }

    /// <inheritdoc/>
    public GoogleTwoFactorProof Unprotect(
        Guid memberId,
        Guid challengeId,
        string protectedProof)
    {
        try
        {
            var plaintext = CreateProtector(
                    memberId,
                    challengeId)
                .Unprotect(protectedProof);
            var proof = JsonSerializer.Deserialize<GoogleTwoFactorProof>(plaintext);

            if (proof?.Context?.Identity is null)
                throw new TwoFactorUnavailableException();

            return proof;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {

            throw new TwoFactorUnavailableException();
        }
    }

    /// <summary>Separates accounts, challenges and protocol versions cryptographically.</summary>
    /// <param name="memberId">The resolved account.</param>
    /// <param name="challengeId">The one-time challenge.</param>
    /// <returns>The purpose-bound authenticated protector.</returns>
    private IDataProtector CreateProtector(
        Guid memberId,
        Guid challengeId)
    {

        return dataProtection.CreateProtector(
            Purpose,
            memberId.ToString("N"),
            challengeId.ToString("N"));
    }
}
