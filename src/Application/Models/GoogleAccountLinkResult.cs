using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the result of an explicit Google account link.
/// </summary>
/// <param name="outcome">The link outcome.</param>
/// <param name="tokens">The MonKado tokens when the link succeeds.</param>
/// <param name="memberId">The linked member identifier after a successful commit.</param>
[ExcludeFromCodeCoverage]
public class GoogleAccountLinkResult(
    GoogleAccountLinkOutcome outcome,
    AccountSessionTokens? tokens,
    Guid? memberId = null)
{
    /// <summary>
    /// Gets the link outcome.
    /// </summary>
    public GoogleAccountLinkOutcome Outcome { get; } = outcome;

    /// <summary>
    /// Gets the MonKado tokens when the link succeeds.
    /// </summary>
    public AccountSessionTokens? Tokens { get; } = tokens;

    /// <summary>
    /// Gets the linked member identifier after a successful commit.
    /// </summary>
    public Guid? MemberId { get; } = memberId;
}
