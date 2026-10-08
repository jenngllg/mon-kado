using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishImportApiFactory : GiftImageApiFactory
{
    public RecordingUrlImportClient ImportClient { get; } = new();
    public bool ProductCatalogEnabled
    {
        get; init;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((
            _,
            configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UrlImportCatalog:Enabled"] = ProductCatalogEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["UrlImportCatalog:IndexKey"] = "key_public_test_index"
            }));
        builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUrlImportClient>();
                services.AddSingleton<IUrlImportClient>(ImportClient);
            });
    }
}
