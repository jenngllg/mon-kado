using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the validated identity claims returned by Google.
/// </summary>
/// <param name="subject">The case-sensitive Google subject.</param>
/// <param name="email">The Google account email address.</param>
/// <param name="emailVerified">Whether Google reports the email address as verified.</param>
/// <param name="hostedDomain">The optional Google Workspace hosted domain.</param>
/// <param name="displayName">The optional Google profile display name.</param>
[ExcludeFromCodeCoverage]
public class GoogleIdentity(
    string? subject,
    string? email,
    bool emailVerified,
    string? hostedDomain,
    string? displayName)
{
    /// <summary>
    /// Gets the case-sensitive Google subject.
    /// </summary>
    public string? Subject { get; } = subject;

    /// <summary>
    /// Gets the Google account email address.
    /// </summary>
    public string? Email { get; } = email;

    /// <summary>
    /// Gets whether Google reports the email address as verified.
    /// </summary>
    public bool EmailVerified { get; } = emailVerified;

    /// <summary>
    /// Gets the optional Google Workspace hosted domain.
    /// </summary>
    public string? HostedDomain { get; } = hostedDomain;

    /// <summary>
    /// Gets the optional Google profile display name.
    /// </summary>
    public string? DisplayName { get; } = displayName;
}
