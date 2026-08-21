using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

internal class StubLookupNormalizer(string? normalizedEmail) : ILookupNormalizer
{
    public int NormalizeEmailCallCount
    {
        get; private set;
    }

    public string? NormalizeEmail(string? email)
    {
        NormalizeEmailCallCount++;

        return normalizedEmail;
    }

    public string? NormalizeName(string? name)
    {
        return name;
    }
}
