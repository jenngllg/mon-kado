using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains validated provider identity and first-factor authority for a deferred association.</summary>
/// <param name="context">The server-validated provider context.</param>
/// <param name="passwordWasVerified">Whether the current local password was verified before issuing the grant.</param>
[ExcludeFromCodeCoverage]
public class GoogleTwoFactorProof(
    GoogleAuthenticationContext context,
    bool passwordWasVerified)
{
    /// <summary>Gets the server-validated Google context, never raw provider tokens.</summary>
    public GoogleAuthenticationContext Context { get; } = context;

    /// <summary>Gets whether the current local password was explicitly verified before issuing the challenge.</summary>
    public bool PasswordWasVerified { get; } = passwordWasVerified;
}
