using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
/// <summary>
/// Represents email confirmation token provider options.
/// </summary>

public class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>
    /// Identifies provider name.
    /// </summary>
    public const string ProviderName = "MonKadoEmailConfirmation";
    /// <summary>
    /// Initializes a new instance of the class.
    /// </summary>

    public EmailConfirmationTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = TimeSpan.FromHours(24);
    }
}
