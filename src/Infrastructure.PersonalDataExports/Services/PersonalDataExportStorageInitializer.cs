using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Services;

/// <summary>Checks the private volume before either host accepts work.</summary>
/// <param name="store">The shared archive store.</param>
public class PersonalDataExportStorageInitializer(IPersonalDataExportStore store) : IHostedService
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {

        return store.InitializeAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {

        return Task.CompletedTask;
    }
}
