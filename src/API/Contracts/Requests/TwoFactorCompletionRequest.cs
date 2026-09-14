using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains untrusted second-factor input validated by the application pipeline.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorCompletionRequest
{
    /// <summary>Gets the opaque sign-in proof.</summary>
    public string? Flow
    {
        get; init;
    }

    /// <summary>Gets the optional authenticator code.</summary>
    public string? Code
    {
        get; init;
    }

    /// <summary>Gets the optional recovery code.</summary>
    public string? RecoveryCode
    {
        get; init;
    }
}
