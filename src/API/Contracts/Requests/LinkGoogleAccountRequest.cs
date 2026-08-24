using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents proof of the current MonKado account for an explicit Google link.
/// </summary>
/// <param name="currentPassword">The exact current MonKado password.</param>
[ExcludeFromCodeCoverage]
public class LinkGoogleAccountRequest(string? currentPassword)
{
    /// <summary>
    /// Gets the exact current MonKado password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;
}
