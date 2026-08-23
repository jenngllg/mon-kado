using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Normalizes Identity lookup keys and guarantees an invariant fallback for non-null values.
/// </summary>
/// <param name="innerNormalizer">The underlying Identity lookup normalizer.</param>
public class InvariantFallbackLookupNormalizer(
    ILookupNormalizer innerNormalizer) : ILookupNormalizer
{
    /// <inheritdoc />
    public string? NormalizeEmail(string? email)
    {

        if (email is null)
            return null;

        return innerNormalizer.NormalizeEmail(email)
            ?? email.Normalize().ToUpperInvariant();
    }

    /// <inheritdoc />
    public string? NormalizeName(string? name)
    {

        if (name is null)
            return null;

        return innerNormalizer.NormalizeName(name)
            ?? name.Normalize().ToUpperInvariant();
    }
}
