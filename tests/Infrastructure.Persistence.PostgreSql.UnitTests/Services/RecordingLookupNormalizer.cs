using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class RecordingLookupNormalizer : ILookupNormalizer
{
    public int EmailCallCount
    {
        get; private set;
    }

    public string? EmailResult
    {
        get; set;
    }

    public string? LastEmail
    {
        get; private set;
    }

    public string? LastName
    {
        get; private set;
    }

    public int NameCallCount
    {
        get; private set;
    }

    public string? NameResult
    {
        get; set;
    }

    public string? NormalizeEmail(string? email)
    {
        EmailCallCount++;
        LastEmail = email;

        return EmailResult;
    }

    public string? NormalizeName(string? name)
    {
        NameCallCount++;
        LastName = name;

        return NameResult;
    }
}
