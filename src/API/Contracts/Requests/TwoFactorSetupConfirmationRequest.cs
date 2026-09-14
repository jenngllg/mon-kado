using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains untrusted second-factor input validated by the application pipeline.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorSetupConfirmationRequest
{
    /// <summary>Gets the authorized setup proof.</summary>
    public string? Flow
    {
        get; init;
    }

    /// <summary>Gets the candidate authenticator code.</summary>
    public string? Code
    {
        get; init;
    }
}
