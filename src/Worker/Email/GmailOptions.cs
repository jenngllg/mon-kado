namespace JennGllg.Fr.MonKado.Back.Worker.Email;

internal sealed class GmailOptions
{
    public const string SectionName = "Gmail";

    public string? SenderAddress { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? RefreshToken { get; init; }
}
