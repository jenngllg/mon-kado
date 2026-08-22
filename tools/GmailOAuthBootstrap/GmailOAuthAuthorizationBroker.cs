using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

/// <summary>
/// Authorizes the dedicated Gmail sender account through Google OAuth.
/// </summary>
public class GmailOAuthAuthorizationBroker : IGmailOAuthAuthorizationBroker
{
    private const string UserName = "mon-kado-authentication-email-sender";
    private readonly Func<
        ClientSecrets,
        IEnumerable<string>,
        string,
        CancellationToken,
        IDataStore,
        ICodeReceiver?,
        Task<UserCredential>> _authorizeAsync;

    /// <summary>
    /// Initializes the broker with the Google authorization implementation.
    /// </summary>
    public GmailOAuthAuthorizationBroker()
        : this(GoogleWebAuthorizationBroker.AuthorizeAsync)
    {
    }

    /// <summary>
    /// Initializes the broker with an authorization implementation.
    /// </summary>
    /// <param name="authorizeAsync">The authorization operation.</param>
    public GmailOAuthAuthorizationBroker(
        Func<
            ClientSecrets,
            IEnumerable<string>,
            string,
            CancellationToken,
            IDataStore,
            ICodeReceiver?,
            Task<UserCredential>> authorizeAsync)
    {
        _authorizeAsync = authorizeAsync;
    }

    /// <inheritdoc />
    public async Task<TokenResponse> AuthorizeAsync(
        ClientSecrets clientSecrets,
        CancellationToken cancellationToken)
    {
        var credential = await _authorizeAsync(
            clientSecrets,
            [GmailService.Scope.GmailSend],
            UserName,
            cancellationToken,
            new MemoryDataStore(),
            null);

        return credential.Token;
    }
}
