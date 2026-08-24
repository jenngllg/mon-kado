using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the result of completing a Google authentication flow.
/// </summary>
/// <param name="outcome">The completion outcome.</param>
/// <param name="session">The refresh-only session when authentication succeeds.</param>
/// <param name="memberId">The resolved member identifier after a successful commit.</param>
/// <param name="memberResolution">How the Google identity resolved after a successful commit.</param>
[ExcludeFromCodeCoverage]
public class GoogleAuthenticationResult(
    GoogleAuthenticationOutcome outcome,
    AccountRefreshSession? session,
    Guid? memberId = null,
    GoogleMemberResolution? memberResolution = null)
{
    /// <summary>
    /// Gets the completion outcome.
    /// </summary>
    public GoogleAuthenticationOutcome Outcome { get; } = outcome;

    /// <summary>
    /// Gets the refresh-only session when authentication succeeds.
    /// </summary>
    public AccountRefreshSession? Session { get; } = session;

    /// <summary>
    /// Gets the resolved member identifier after a successful commit.
    /// </summary>
    public Guid? MemberId { get; } = memberId;

    /// <summary>
    /// Gets how the Google identity resolved after a successful commit.
    /// </summary>
    public GoogleMemberResolution? MemberResolution { get; } = memberResolution;
}
