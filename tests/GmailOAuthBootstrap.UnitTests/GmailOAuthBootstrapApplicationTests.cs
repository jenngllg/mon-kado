using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;

using JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap.UnitTests;

public class GmailOAuthBootstrapApplicationTests : IDisposable
{
    private readonly Mock<IGmailOAuthAuthorizationBroker> _authorizationBrokerMock =
        new(MockBehavior.Strict);
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("client-id", null)]
    [InlineData(null, "client-secret")]
    public async Task RunAsync_WhenCredentialsAreMissing_ReturnsConfigurationError(
        string? clientId,
        string? clientSecret)
    {
        // Arrange
        var application = CreateApplication(
            clientId,
            clientSecret);

        // Act
        var result = await application.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            result);
        Assert.Contains(
            GmailOAuthBootstrapApplication.ClientIdVariable,
            _error.ToString(),
            StringComparison.Ordinal);
        _authorizationBrokerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenRefreshTokenIsMissing_ReturnsAuthorizationError()
    {
        // Arrange
        _authorizationBrokerMock
            .Setup(broker => broker.AuthorizeAsync(
                It.Is<ClientSecrets>(secrets =>
                    secrets.ClientId == "client-id" &&
                    secrets.ClientSecret == "client-secret"),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new TokenResponse());
        var application = CreateApplication(
            "client-id",
            "client-secret");

        // Act
        var result = await application.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            result);
        Assert.Contains(
            "did not return a refresh token",
            _error.ToString(),
            StringComparison.Ordinal);
        _authorizationBrokerMock.Verify(broker => broker.AuthorizeAsync(
            It.IsAny<ClientSecrets>(),
            TestContext.Current.CancellationToken), Times.Once);
        _authorizationBrokerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_WhenAuthorizationSucceeds_PrintsRefreshTokenAndReturnsSuccess()
    {
        // Arrange
        _authorizationBrokerMock
            .Setup(broker => broker.AuthorizeAsync(
                It.IsAny<ClientSecrets>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new TokenResponse { RefreshToken = "refresh-token" });
        var application = CreateApplication(
            "client-id",
            "client-secret");

        // Act
        var result = await application.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            result);
        Assert.Contains(
            "refresh-token",
            _output.ToString(),
            StringComparison.Ordinal);
        _authorizationBrokerMock.Verify(broker => broker.AuthorizeAsync(
            It.IsAny<ClientSecrets>(),
            TestContext.Current.CancellationToken), Times.Once);
        _authorizationBrokerMock.VerifyNoOtherCalls();
    }

    private GmailOAuthBootstrapApplication CreateApplication(
        string? clientId,
        string? clientSecret)
    {

        return new(
            _authorizationBrokerMock.Object,
            variable => variable == GmailOAuthBootstrapApplication.ClientIdVariable
                ? clientId
                : clientSecret,
            _output,
            _error);
    }
}
