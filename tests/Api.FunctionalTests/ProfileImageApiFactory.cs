using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>Hosts profile-photo contracts with a real isolated image store.</summary>
public class ProfileImageApiFactory : GiftImageApiFactory
{
    public RecordingProfileImageService ProfileImageService { get; } = new();

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProfileImageService>();
                services.AddSingleton<IProfileImageService>(ProfileImageService);
            });
    }
}
