using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>
/// Defines named API authorization policies.
/// </summary>
[ExcludeFromCodeCoverage]
public static class AuthorizationPolicies
{
    #region Account

    /// <summary>
    /// Identifies the policy for an authenticated current member session.
    /// </summary>
    public const string CurrentSession = "CurrentSession";

    #endregion
}
