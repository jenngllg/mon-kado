using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains only the browser proof returned by the validated Google callback.</summary>
/// <param name="flow">The canonical 256-bit Base64URL browser binding.</param>
[ExcludeFromCodeCoverage]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CompleteGoogleSessionRequest(string? flow)
{
    /// <summary>Gets the opaque browser-flow binding.</summary>
    public string? Flow { get; } = flow;
}
