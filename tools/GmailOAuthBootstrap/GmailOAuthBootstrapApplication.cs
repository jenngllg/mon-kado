using Google.Apis.Auth.OAuth2;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

internal class GmailOAuthBootstrapApplication(
    IGmailOAuthAuthorizationBroker authorizationBroker,
    Func<string, string?> getEnvironmentVariable,
    TextWriter output,
    TextWriter error)
{
    internal const string ClientIdVariable = "GMAIL_CLIENT_ID";
    internal const string ClientSecretVariable = "GMAIL_CLIENT_SECRET";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var clientId = getEnvironmentVariable(ClientIdVariable) ?? string.Empty;
        var clientSecret = getEnvironmentVariable(ClientSecretVariable) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            await error.WriteLineAsync(
                $"Set {ClientIdVariable} and {ClientSecretVariable} for this process before running the tool.");

            return 1;
        }

        var token = await authorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            await error.WriteLineAsync(
                "Google did not return a refresh token. Revoke the existing grant and authorize again.");

            return 2;
        }

        await output.WriteLineAsync(
            "Store this refresh token as a secret. It will not be written to disk:");
        await output.WriteLineAsync(token.RefreshToken);

        return 0;
    }
}
