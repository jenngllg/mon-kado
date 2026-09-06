namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

/// <summary>Bounds remote import work and memory consumption.</summary>
public class UrlImportOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "UrlImport";
    /// <summary>Gets the total preview budget in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 20;
    /// <summary>Gets the maximum decompressed HTML length.</summary>
    public int MaximumHtmlBytes { get; init; } = 2 * 1024 * 1024;
    /// <summary>Gets the maximum number of manually validated redirects.</summary>
    public int MaximumRedirects { get; init; } = 3;
    /// <summary>Gets the per-member preview quota per minute.</summary>
    public int PermitLimit { get; init; } = 10;
}
