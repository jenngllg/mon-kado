using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GoogleOpenIdConnectConfigurationManagerTests
{
    private readonly Mock<IConfigurationManager<OpenIdConnectConfiguration>> _innerManagerMock;
    private readonly GoogleOpenIdConnectConfigurationManager _manager;

    public GoogleOpenIdConnectConfigurationManagerTests()
    {
        _innerManagerMock = new Mock<IConfigurationManager<OpenIdConnectConfiguration>>(
            MockBehavior.Strict);
        _manager = new GoogleOpenIdConnectConfigurationManager(_innerManagerMock.Object);
    }

    [Fact]
    public async Task GetConfigurationAsync_WhenProviderReturnsConfiguration_ReturnsIt()
    {
        // Arrange
        var configuration = new OpenIdConnectConfiguration();
        var cancellationToken = TestContext.Current.CancellationToken;
        _innerManagerMock
            .Setup(manager => manager.GetConfigurationAsync(cancellationToken))
            .ReturnsAsync(configuration);

        // Act
        var result = await _manager.GetConfigurationAsync(cancellationToken);

        // Assert
        Assert.Same(
            configuration,
            result);
        _innerManagerMock.Verify(
            manager => manager.GetConfigurationAsync(cancellationToken),
            Times.Once);
        _innerManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetConfigurationAsync_WhenRequestIsCancelled_PreservesCancellation()
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var cancellationToken = cancellationSource.Token;
        var exception = new OperationCanceledException(cancellationToken);
        _innerManagerMock
            .Setup(manager => manager.GetConfigurationAsync(cancellationToken))
            .ThrowsAsync(exception);

        // Act
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => _manager.GetConfigurationAsync(cancellationToken));

        // Assert
        Assert.Same(
            exception,
            thrown);
        _innerManagerMock.Verify(
            manager => manager.GetConfigurationAsync(cancellationToken),
            Times.Once);
        _innerManagerMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetConfigurationAsync_WhenProviderFails_ClassifiesDependencyUnavailable(
        bool usesUnrelatedCancellation)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var providerException = CreateProviderException(usesUnrelatedCancellation);
        _innerManagerMock
            .Setup(manager => manager.GetConfigurationAsync(cancellationToken))
            .ThrowsAsync(providerException);

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(
            () => _manager.GetConfigurationAsync(cancellationToken));

        // Assert
        Assert.Same(
            providerException,
            exception.InnerException);
        _innerManagerMock.Verify(
            manager => manager.GetConfigurationAsync(cancellationToken),
            Times.Once);
        _innerManagerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void RequestRefresh_WhenCalled_ForwardsToNativeManager()
    {
        // Arrange
        _innerManagerMock
            .Setup(manager => manager.RequestRefresh());

        // Act
        _manager.RequestRefresh();

        // Assert
        _innerManagerMock.Verify(
            manager => manager.RequestRefresh(),
            Times.Once);
        _innerManagerMock.VerifyNoOtherCalls();
    }

    private static Exception CreateProviderException(bool usesUnrelatedCancellation)
    {

        if (usesUnrelatedCancellation)
            return new OperationCanceledException();

        return new HttpRequestException("Discovery is unavailable.");
    }
}
