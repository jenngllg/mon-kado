using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the protected MonKado context associated with a validated Google identity.
/// </summary>
/// <param name="identity">The validated Google identity.</param>
/// <param name="isPersistent">Whether the requested MonKado session is persistent.</param>
/// <param name="returnPath">The validated frontend return path.</param>
/// <param name="flowId">The one-time Google authentication flow identifier.</param>
/// <param name="expectedMemberId">The member resolved before the external cookie was issued, when one existed.</param>
/// <param name="currentSessionId">The optional refresh session proven when the flow started.</param>
[ExcludeFromCodeCoverage]
public class GoogleAuthenticationContext(
    GoogleIdentity identity,
    bool isPersistent,
    string returnPath,
    Guid flowId,
    Guid? expectedMemberId,
    Guid? currentSessionId)
{
    /// <summary>
    /// Gets the validated Google identity.
    /// </summary>
    public GoogleIdentity Identity { get; } = identity;

    /// <summary>
    /// Gets whether the requested MonKado session is persistent.
    /// </summary>
    public bool IsPersistent { get; } = isPersistent;

    /// <summary>
    /// Gets the validated frontend return path.
    /// </summary>
    public string ReturnPath { get; } = returnPath;

    /// <summary>
    /// Gets the one-time Google authentication flow identifier.
    /// </summary>
    public Guid FlowId { get; } = flowId;

    /// <summary>
    /// Gets the member resolved before the external cookie was issued, when one existed.
    /// </summary>
    public Guid? ExpectedMemberId { get; } = expectedMemberId;

    /// <summary>
    /// Gets the optional refresh session proven when the flow started.
    /// </summary>
    public Guid? CurrentSessionId { get; } = currentSessionId;
}
