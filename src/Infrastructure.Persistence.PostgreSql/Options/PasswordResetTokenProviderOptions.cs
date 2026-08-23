using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>
/// Configures member password reset tokens.
/// </summary>
public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>
    /// Identifies the provider name.
    /// </summary>
    public const string ProviderName = "MonKadoPasswordReset";

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetTokenProviderOptions" /> class.
    /// </summary>
    public PasswordResetTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = TimeSpan.FromHours(1);
    }
}
