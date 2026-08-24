using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Models;

/// <summary>
/// Contains a protected Google authentication context and its opaque browser-flow binding.
/// </summary>
[ExcludeFromCodeCoverage]
public class GoogleExternalAuthenticationTicket(
    GoogleAuthenticationContext context,
    string flowBinding)
{
    /// <summary>
    /// Gets the protected application authentication context.
    /// </summary>
    public GoogleAuthenticationContext Context { get; } = context;

    /// <summary>
    /// Gets the opaque browser-flow binding.
    /// </summary>
    public string FlowBinding { get; } = flowBinding;
}
