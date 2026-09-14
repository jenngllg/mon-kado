using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains the candidate authenticator material, exposed only to an authorized setup flow.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorSetupResponse
{
    /// <summary>Gets the Base32 key for manual authenticator enrollment.</summary>
    public string ManualKey { get; init; } = string.Empty;

    /// <summary>Gets the URI that the frontend can encode as a QR code without sending it to a third party.</summary>
    public string OtpAuthUri { get; init; } = string.Empty;
}
