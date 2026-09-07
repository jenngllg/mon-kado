using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents proof of the current MonKado account for an explicit Google link.
/// </summary>
/// <param name="currentPassword">The exact current MonKado password.</param>
/// <param name="flow">The canonical 256-bit Base64URL browser binding.</param>
[ExcludeFromCodeCoverage]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class LinkGoogleAccountRequest(
    string? currentPassword,
    string? flow)
{
    /// <summary>Gets the opaque browser-flow binding.</summary>
    public string? Flow { get; } = flow;

    /// <summary>
    /// Gets the exact current MonKado password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;
}
