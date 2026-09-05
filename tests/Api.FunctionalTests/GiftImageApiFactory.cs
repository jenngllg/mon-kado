using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Hosts gift-image functional tests with an isolated real filesystem store.
/// </summary>
public class GiftImageApiFactory(IPAddress? remoteIpAddress = null)
    : RegistrationApiFactory(remoteIpAddress: remoteIpAddress)
{
    /// <summary>Gets the isolated image-storage path.</summary>
    public string StoragePath
    {
        get;
    } = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-gift-image-functional-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>Gets the recording current-state image access service.</summary>
    public RecordingWishImageAccessService WishImageAccessService { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting(
            "GiftImages:StoragePath",
            StoragePath);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWishImageAccessService>();
            services.AddSingleton<IWishImageAccessService>(WishImageAccessService);
        });
    }

    /// <summary>
    /// Disposes the host and removes its isolated image files.
    /// </summary>
    /// <returns>A task that represents the asynchronous cleanup.</returns>
    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (Directory.Exists(StoragePath))
            Directory.Delete(
                StoragePath,
                recursive: true);
    }
}
