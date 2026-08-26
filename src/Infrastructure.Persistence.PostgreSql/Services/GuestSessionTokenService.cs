using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.IdentityModel.Tokens;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates and validates opaque 256-bit guest-session tokens.
/// </summary>
public class GuestSessionTokenService : IGuestSessionTokenService
{
    private const int SecretSize = 32;
    private const int TokenPartCount = 2;
    private const char Separator = '.';

    /// <inheritdoc />
    public GuestSessionToken Create(Guid sessionId)
    {
        var secretBytes = RandomNumberGenerator.GetBytes(SecretSize);
        var encodedSecret = Base64UrlEncoder.Encode(secretBytes);
        var token = string.Concat(
            sessionId.ToString("N"),
            Separator,
            encodedSecret);

        return new GuestSessionToken(
            sessionId,
            token,
            SHA256.HashData(secretBytes));
    }

    /// <inheritdoc />
    public bool TryParse(
        string token,
        out Guid sessionId,
        out byte[] secretHash)
    {
        sessionId = Guid.Empty;
        secretHash = [];
        var parts = token.Split(
            Separator,
            TokenPartCount,
            StringSplitOptions.None);

        if (parts.Length != TokenPartCount ||
            !Guid.TryParseExact(
                parts[0],
                "N",
                out sessionId))
        {
            return false;
        }

        try
        {
            var secretBytes = Base64UrlEncoder.DecodeBytes(parts[1]);

            if (secretBytes.Length != SecretSize)
                return false;

            secretHash = SHA256.HashData(secretBytes);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool Verify(
        byte[] presentedHash,
        byte[] persistedHash)
    {
        return presentedHash.Length == SHA256.HashSizeInBytes &&
            persistedHash.Length == SHA256.HashSizeInBytes &&
            CryptographicOperations.FixedTimeEquals(
                presentedHash,
                persistedHash);
    }
}
