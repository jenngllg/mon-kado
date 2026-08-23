using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a request to confirm a member email change.
/// </summary>
/// <param name="requestId">The email change request identifier.</param>
/// <param name="token">The email change confirmation token.</param>
[ExcludeFromCodeCoverage]
public class ConfirmMemberEmailChangeRequest(
    Guid? requestId,
    string? token)
{
    /// <summary>
    /// Gets the email change request identifier.
    /// </summary>
    public Guid? RequestId { get; } = requestId;

    /// <summary>
    /// Gets the email change confirmation token.
    /// </summary>
    public string? Token { get; } = token;
}
