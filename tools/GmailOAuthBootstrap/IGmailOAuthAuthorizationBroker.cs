using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

internal interface IGmailOAuthAuthorizationBroker
{
    Task<TokenResponse> AuthorizeAsync(
        ClientSecrets clientSecrets,
        CancellationToken cancellationToken);
}
