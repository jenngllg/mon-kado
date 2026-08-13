using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;

const string ClientIdVariable = "GMAIL_CLIENT_ID";
const string ClientSecretVariable = "GMAIL_CLIENT_SECRET";

string clientId = Environment.GetEnvironmentVariable(ClientIdVariable) ?? string.Empty;
string clientSecret = Environment.GetEnvironmentVariable(ClientSecretVariable) ?? string.Empty;
if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    Console.Error.WriteLine(
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
    Console.Error.WriteLine(
        "Google did not return a refresh token. Revoke the existing grant and authorize again.");
    return 2;
}

Console.WriteLine("Store this refresh token as a secret. It will not be written to disk:");
Console.WriteLine(credential.Token.RefreshToken);
return 0;

internal sealed class MemoryDataStore : IDataStore
{
    private readonly ConcurrentDictionary<string, object> values = new(StringComparer.Ordinal);

    public Task ClearAsync()
    {
        values.Clear();
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        values.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        return Task.FromResult(values.TryGetValue(key, out object? value) ? (T)value : default);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        values[key] = value!;
        return Task.CompletedTask;
    }
}
