using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains transient challenge material; only its hash may be persisted.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorFlowToken
{
    /// <summary>Gets the unpadded Base64URL browser proof.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Gets the durable SHA-256 hash.</summary>
    public byte[] Hash { get; init; } = [];
}
