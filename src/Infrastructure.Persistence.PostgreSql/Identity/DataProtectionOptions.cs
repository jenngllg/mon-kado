namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string? KeysPath { get; init; }
}
