using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains the token explicitly submitted to confirm account deletion.</summary>
/// <param name="token">The protected token received by email.</param>
[ExcludeFromCodeCoverage]
public class ConfirmMemberAccountDeletionRequest(string? token)
{
    /// <summary>Gets the protected confirmation token.</summary>
    public string? Token { get; } = token;
}
