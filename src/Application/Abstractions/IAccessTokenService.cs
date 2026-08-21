using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Creates signed access tokens for authenticated members.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// Creates an access token for a member.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <returns>The signed access token.</returns>
    AccessToken Create(Guid userId);
}
