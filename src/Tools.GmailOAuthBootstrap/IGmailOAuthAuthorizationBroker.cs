using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

/// <summary>
/// Authorizes Gmail access and returns its OAuth token response.
/// </summary>
public interface IGmailOAuthAuthorizationBroker
{
    /// <summary>
    /// Authorizes Gmail access.
    /// </summary>
    /// <param name="clientSecrets">The OAuth client secrets.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The OAuth token response.</returns>
    Task<TokenResponse> AuthorizeAsync(
        ClientSecrets clientSecrets,
        CancellationToken cancellationToken);
}
