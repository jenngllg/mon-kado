using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains untrusted second-factor input validated by the application pipeline.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorFlowRequest
{
    /// <summary>Gets the opaque, operation-bound proof.</summary>
    public string? Flow
    {
        get; init;
    }
}
