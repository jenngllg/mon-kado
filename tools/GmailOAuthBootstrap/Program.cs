using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

const string ClientIdVariable = "GMAIL_CLIENT_ID";
const string ClientSecretVariable = "GMAIL_CLIENT_SECRET";

string clientId = Environment.GetEnvironmentVariable(ClientIdVariable) ?? string.Empty;
string clientSecret = Environment.GetEnvironmentVariable(ClientSecretVariable) ?? string.Empty;
if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    await Console.Error.WriteLineAsync(
        $"Set {ClientIdVariable} and {ClientSecretVariable} for this process before running the tool.");
    return 1;
}

UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
    new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
    [GmailService.Scope.GmailSend],
    "mon-kado-authentication-email-sender",
    CancellationToken.None,
    new MemoryDataStore());

if (string.IsNullOrWhiteSpace(credential.Token.RefreshToken))
{
    await Console.Error.WriteLineAsync(
        "Google did not return a refresh token. Revoke the existing grant and authorize again.");
    return 2;
}

await Console.Out.WriteLineAsync("Store this refresh token as a secret. It will not be written to disk:");
await Console.Out.WriteLineAsync(credential.Token.RefreshToken);
return 0;
