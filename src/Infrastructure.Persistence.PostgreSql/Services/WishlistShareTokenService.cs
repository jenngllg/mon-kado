using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates and verifies cryptographic wishlist share-link secrets.
/// </summary>
public class WishlistShareTokenService : IWishlistShareTokenService
{
    private const int SecretLength = 32;
    private readonly IDataProtector _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="WishlistShareTokenService" /> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The shared data-protection provider.</param>
    public WishlistShareTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(
            "MonKado.WishlistShareLink.Secret.v1");
    }

    /// <inheritdoc />
    public WishlistShareToken Create()
    {
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretLength));

        return new WishlistShareToken(
            secret,
            Hash(secret),
            _protector.Protect(secret));
    }

    /// <inheritdoc />
    public string Unprotect(string protectedSecret)
    {
        return _protector.Unprotect(protectedSecret);
    }

    /// <inheritdoc />
    public bool Verify(
        string secret,
        byte[] expectedHash)
    {
        if (expectedHash.Length != SHA256.HashSizeInBytes || !HasValidFormat(secret))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Hash(secret),
            expectedHash);
    }

    /// <summary>
    /// Determines whether a secret is a 256-bit Base64Url value.
    /// </summary>
    /// <param name="secret">The presented secret.</param>
    /// <returns><see langword="true" /> when the format and length are valid.</returns>
    private static bool HasValidFormat(string secret)
    {
        try
        {
            return WebEncoders.Base64UrlDecode(secret).Length == SecretLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of a share-link secret.
    /// </summary>
    /// <param name="secret">The secret to hash.</param>
    /// <returns>The 256-bit hash.</returns>
    private static byte[] Hash(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}
