using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Configurations;

public class UrlImportInjectionConfigurationTests
{
    [Fact]
    public async Task ConfigureUrlImportInjection_WhenClientResolvesLoopback_RejectsBeforeOpeningConnection()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton(TimeProvider.System);
        var processorMock = new Mock<IGiftImageProcessor>(MockBehavior.Strict);
        services.AddSingleton(processorMock.Object);
        services.ConfigureUrlImportInjection(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IUrlImportClient>();

        // Act
        var exception = await Record.ExceptionAsync(() => client.DownloadAsync(
                new Uri("http://127.0.0.1:80/"),
                1024,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<WishImportUrlRejectedException>(exception);
        Assert.IsType<WishImportService>(scope.ServiceProvider.GetRequiredService<IWishImportService>());
        Assert.IsType<ImportSocketConnector>(scope.ServiceProvider.GetRequiredService<IImportSocketConnector>());
        Assert.IsType<MerchantMetadataExtractor>(scope.ServiceProvider.GetRequiredService<IMerchantMetadataExtractor>());
        processorMock.VerifyNoOtherCalls();
    }
}
