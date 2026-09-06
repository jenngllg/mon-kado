using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishImportApiFactory : GiftImageApiFactory
{
    public RecordingUrlImportClient ImportClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUrlImportClient>();
                services.AddSingleton<IUrlImportClient>(ImportClient);
            });
    }
}
