using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Encodes and decodes authentication email tokens for browser links.
/// </summary>
public static class AuthenticationEmailTokenEncoding
{
    private static readonly UTF8Encoding _strictUtf8 = new(
        false,
        true);

    /// <summary>
    /// Encodes an Identity token as Base64 URL text.
    /// </summary>
    /// <param name="token">The Identity token.</param>
    /// <returns>The encoded token.</returns>
    public static string Encode(string token)
    {

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token))
            .TrimEnd('=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
    }

    /// <summary>
    /// Decodes Base64 URL text into an Identity token.
    /// </summary>
    /// <param name="token">The encoded token.</param>
    /// <param name="decodedToken">The decoded Identity token.</param>
    /// <returns><see langword="true" /> when decoding succeeds.</returns>
    public static bool TryDecode(
        string token,
        out string decodedToken)
    {
        decodedToken = string.Empty;
        try
        {
            var base64 = token
                .Replace(
                    '-',
                    '+')
                .Replace(
                    '_',
                    '/');
            var remainder = base64.Length % 4;

            if (remainder == 1)
                return false;

            if (remainder > 0)
                base64 = base64.PadRight(
                    base64.Length + 4 - remainder,
                    '=');

            decodedToken = _strictUtf8.GetString(Convert.FromBase64String(base64));

            return decodedToken.Length > 0;
        }
        catch (FormatException)
        {

            return false;
        }
        catch (DecoderFallbackException)
        {

            return false;
        }
    }
}
