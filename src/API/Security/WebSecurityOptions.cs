namespace JennGllg.Fr.MonKado.Back.Api.Security;

public sealed class WebSecurityOptions
{
    public const string SectionName = "WebSecurity";

    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    public string[] AllowedOrigins { get; init; } = [];

    public string? DataProtectionKeysPath { get; init; }
}
