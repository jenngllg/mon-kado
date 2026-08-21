using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class ResettableAuthenticationHandlerProviderTests
{
    private const string SchemeName = "Application";

    private readonly Mock<IAuthenticationSchemeProvider> _schemeProviderMock =
        new(MockBehavior.Strict);

    [Fact]
    public async Task GetHandlerAsync_WhenSchemeDoesNotExist_ReturnsNull()
    {
        // Arrange
        _schemeProviderMock
            .Setup(provider => provider.GetSchemeAsync(SchemeName))
            .ReturnsAsync((AuthenticationScheme?)null);
        var provider = new ResettableAuthenticationHandlerProvider(_schemeProviderMock.Object);
        var context = CreateContext(new ServiceCollection());

        // Act
        var result = await provider.GetHandlerAsync(
            context,
            SchemeName);

        // Assert
        Assert.Null(result);
        _schemeProviderMock.Verify(
            value => value.GetSchemeAsync(SchemeName),
            Times.Once);
        _schemeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetHandlerAsync_WhenHandlerIsRegistered_InitializesAndCachesHandler()
    {
        // Arrange
        var scheme = CreateScheme(typeof(TestAuthenticationHandler));
        _schemeProviderMock
            .Setup(provider => provider.GetSchemeAsync(SchemeName))
            .ReturnsAsync(scheme);
        var handler = new TestAuthenticationHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var context = CreateContext(services);
        var provider = new ResettableAuthenticationHandlerProvider(_schemeProviderMock.Object);

        // Act
        var first = await provider.GetHandlerAsync(
            context,
            SchemeName);
        var second = await provider.GetHandlerAsync(
            context,
            SchemeName);

        // Assert
        Assert.Same(
            handler,
            first);
        Assert.Same(
            first,
            second);
        Assert.Equal(
            1,
            handler.InitializationCount);
        _schemeProviderMock.Verify(
            value => value.GetSchemeAsync(SchemeName),
            Times.Once);
        _schemeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetHandlerAsync_WhenHandlerIsNotRegistered_CreatesHandler()
    {
        // Arrange
        var scheme = CreateScheme(typeof(TestAuthenticationHandler));
        _schemeProviderMock
            .Setup(provider => provider.GetSchemeAsync(SchemeName))
            .ReturnsAsync(scheme);
        var context = CreateContext(new ServiceCollection());
        var provider = new ResettableAuthenticationHandlerProvider(_schemeProviderMock.Object);

        // Act
        var result = await provider.GetHandlerAsync(
            context,
            SchemeName);

        // Assert
        var handler = Assert.IsType<TestAuthenticationHandler>(result);
        Assert.Equal(
            1,
            handler.InitializationCount);
        _schemeProviderMock.Verify(
            value => value.GetSchemeAsync(SchemeName),
            Times.Once);
        _schemeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reset_WhenHandlerIsCached_CreatesHandlerAgain()
    {
        // Arrange
        var scheme = CreateScheme(typeof(TestAuthenticationHandler));
        _schemeProviderMock
            .Setup(provider => provider.GetSchemeAsync(SchemeName))
            .ReturnsAsync(scheme);
        var services = new ServiceCollection();
        services.AddTransient<TestAuthenticationHandler>();
        var context = CreateContext(services);
        var provider = new ResettableAuthenticationHandlerProvider(_schemeProviderMock.Object);
        var first = await provider.GetHandlerAsync(
            context,
            SchemeName);

        // Act
        provider.Reset(SchemeName);
        var second = await provider.GetHandlerAsync(
            context,
            SchemeName);

        // Assert
        Assert.NotSame(
            first,
            second);
        _schemeProviderMock.Verify(
            value => value.GetSchemeAsync(SchemeName),
            Times.Exactly(2));
        _schemeProviderMock.VerifyNoOtherCalls();
    }

    private static AuthenticationScheme CreateScheme(Type handlerType)
    {

        return new AuthenticationScheme(
            SchemeName,
            displayName: null,
            handlerType);
    }

    private static DefaultHttpContext CreateContext(IServiceCollection services)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        return context;
    }
}
