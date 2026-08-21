using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;

using JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap.UnitTests;

public class GmailOAuthAuthorizationBrokerTests
{
    [Fact]
    public void Constructor_WhenDefaultAuthorizationIsUsed_CreatesBroker()
    {
        // Arrange
        // Act
        var broker = new GmailOAuthAuthorizationBroker();

        // Assert
        Assert.NotNull(broker);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenAuthorizationCompletes_ReturnsToken()
    {
        // Arrange
        ClientSecrets? capturedSecrets = null;
        IEnumerable<string>? capturedScopes = null;
        string? capturedUserName = null;
        IDataStore? capturedDataStore = null;
        ICodeReceiver? capturedCodeReceiver = new LocalServerCodeReceiver();
        var token = new TokenResponse { RefreshToken = "refresh-token" };
        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = "flow-client-id",
                ClientSecret = "flow-client-secret"
            }
        });
        var credential = new UserCredential(
            flow,
            "test-user",
            token);
        var broker = new GmailOAuthAuthorizationBroker((
            secrets,
            scopes,
            userName,
            _,
            dataStore,
            codeReceiver) =>
        {
            capturedSecrets = secrets;
            capturedScopes = scopes;
            capturedUserName = userName;
            capturedDataStore = dataStore;
            capturedCodeReceiver = codeReceiver;

            return Task.FromResult(credential);
        });
        var clientSecrets = new ClientSecrets
        {
            ClientId = "client-id",
            ClientSecret = "client-secret"
        };

        // Act
        var result = await broker.AuthorizeAsync(
            clientSecrets,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(
            token,
            result);
        Assert.Same(
            clientSecrets,
            capturedSecrets);
        Assert.Single(capturedScopes!);
        Assert.Equal(
            "mon-kado-authentication-email-sender",
            capturedUserName);
        Assert.IsType<MemoryDataStore>(capturedDataStore);
        Assert.Null(capturedCodeReceiver);
    }
}
