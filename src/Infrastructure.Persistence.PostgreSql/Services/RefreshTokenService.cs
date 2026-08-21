using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.WebUtilities;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates, parses and verifies refresh tokens.
/// </summary>
internal class RefreshTokenService : IRefreshTokenService
{
    private const int SecretLength = 32;
    private const int TokenPartCount = 2;

    /// <summary>
    /// Creates refresh token material for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The refresh token and its hash.</returns>
    public RefreshToken Create(Guid sessionId)
    {
        var secret = RandomNumberGenerator.GetBytes(SecretLength);
        var value = string.Join(
            '.',
            sessionId.ToString("N"),
            WebEncoders.Base64UrlEncode(secret));

        return new RefreshToken(
            value,
            Hash(value));
    }

    /// <summary>
    /// Attempts to extract the session identifier from a refresh token.
    /// </summary>
    /// <param name="value">The refresh token.</param>
    /// <param name="sessionId">The extracted session identifier.</param>
    /// <returns><see langword="true" /> when the token has a valid format.</returns>
    public bool TryGetSessionId(
        string value,
        out Guid sessionId)
    {
        sessionId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('.');

        if (parts.Length != TokenPartCount ||
            !Guid.TryParseExact(
                parts[0],
                "N",
                out sessionId))
            return false;

        try
        {
            return WebEncoders.Base64UrlDecode(parts[1]).Length == SecretLength;
        }
        catch (FormatException)
        {
            sessionId = Guid.Empty;

            return false;
        }
    }

    /// <summary>
    /// Verifies a refresh token against a stored hash.
    /// </summary>
    /// <param name="value">The refresh token.</param>
    /// <param name="expectedHash">The expected hash.</param>
    /// <returns><see langword="true" /> when the token matches the hash.</returns>
    public bool Verify(
        string value,
        byte[] expectedHash)
    {
        if (expectedHash.Length != SHA256.HashSizeInBytes)
            return false;

        var actualHash = Hash(value);

        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHash);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a refresh token.
    /// </summary>
    /// <param name="value">The refresh token.</param>
    /// <returns>The token hash.</returns>
    internal static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
