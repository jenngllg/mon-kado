using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains newly generated recovery codes that cannot be retrieved again.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorRecoveryCodesResponse
{
    /// <summary>Gets the ten plaintext recovery codes shown only after their generation is committed.</summary>
    public IEnumerable<string> RecoveryCodes { get; init; } = [];
}
