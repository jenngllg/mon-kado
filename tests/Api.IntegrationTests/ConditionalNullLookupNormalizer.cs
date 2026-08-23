using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class ConditionalNullLookupNormalizer : ILookupNormalizer
{
    public const string NullEmail = "null-normalizer@example.fr";

    public string? NormalizeEmail(string? email)
    {

        if (email == NullEmail)
            return null;

        return email?.Normalize().ToUpperInvariant();
    }

    public string? NormalizeName(string? name)
    {

        return name?.Normalize().ToUpperInvariant();
    }
}
