using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

/// <summary>
/// Creates complete Google identities shared by command-validator tests.
/// </summary>
public static class GoogleAuthenticationValidatorTestData
{
    /// <summary>
    /// Creates a valid Gmail identity.
    /// </summary>
    /// <returns>The valid identity.</returns>
    public static GoogleIdentity CreateValidIdentity()
    {

        return new GoogleIdentity(
            "subject",
            "member@gmail.com",
            true,
            null,
            null);
    }
}
