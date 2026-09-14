using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains untrusted second-factor input validated by the application pipeline.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorReauthenticationRequest
{
    /// <summary>Gets the requested management operation.</summary>
    public TwoFactorFlowPurpose? Purpose
    {
        get; init;
    }

    /// <summary>Gets the current authenticator code.</summary>
    public string? Code
    {
        get; init;
    }

    /// <summary>Gets the recovery code permitted only for forced replacement.</summary>
    public string? RecoveryCode
    {
        get; init;
    }
}
