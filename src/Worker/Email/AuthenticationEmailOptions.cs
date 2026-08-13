namespace JennGllg.Fr.MonKado.Back.Worker.Email;

internal sealed class AuthenticationEmailOptions
{
    public const string SectionName = "AuthenticationEmail";
    public const string DisabledProvider = "Disabled";
    public const string GmailProvider = "Gmail";

    public string Provider { get; init; } = DisabledProvider;

    public string? FrontendOrigin { get; init; }

    public bool IsEnabled => Provider.Equals(GmailProvider, StringComparison.Ordinal);
}
