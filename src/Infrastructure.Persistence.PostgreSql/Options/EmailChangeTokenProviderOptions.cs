using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>
/// Configures request-specific member email change tokens.
/// </summary>
public class EmailChangeTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>
    /// Identifies the provider name.
    /// </summary>
    public const string ProviderName = "MonKadoEmailChange";

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailChangeTokenProviderOptions" /> class.
    /// </summary>
    public EmailChangeTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = TimeSpan.FromHours(24);
    }
}
