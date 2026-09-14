using OtpNet;

using System.Globalization;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Creates explicit authenticator proofs for integration scenarios without real-time waits.</summary>
public static class TwoFactorTestData
{
    /// <summary>Creates the authenticator code for the test-controlled timestamp.</summary>
    /// <param name="secret">The manual enrollment key returned by the API.</param>
    /// <param name="clock">The test-controlled clock.</param>
    /// <returns>The exact current six-digit code.</returns>
    public static string CreateCurrentCode(
        string secret,
        TimeProvider clock)
    {
        var authenticator = new Totp(Base32Encoding.ToBytes(secret));

        return authenticator.ComputeTotp(clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>Creates a numeric code guaranteed not to match any accepted interval.</summary>
    /// <param name="secret">The manual enrollment key returned by the API.</param>
    /// <param name="clock">The test-controlled clock.</param>
    /// <returns>A well-formed but cryptographically incorrect code.</returns>
    public static string CreateIncorrectCode(
        string secret,
        TimeProvider clock)
    {
        var authenticator = new Totp(Base32Encoding.ToBytes(secret));
        var accepted = Enumerable.Range(
                -1,
                3)
            .Select(offset => authenticator.ComputeTotp(clock.GetUtcNow().UtcDateTime.AddSeconds(offset * 30)))
            .ToHashSet(StringComparer.Ordinal);

        return Enumerable.Range(
                0,
                4)
            .Select(value => value.ToString(
                "D6",
                CultureInfo.InvariantCulture))
            .First(code => !accepted.Contains(code));
    }
}
