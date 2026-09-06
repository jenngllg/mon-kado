using JennGllg.Fr.MonKado.Back.Application.Configurations;
using JennGllg.Fr.MonKado.Back.Domain.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Worker.Configurations;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker;

/// <summary>
/// Provides the worker process entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Starts the worker process.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>A task that represents the worker lifetime.</returns>
    public static Task Main(string[] args)
    {

        return RunAsync(
            args,
            CancellationToken.None);
    }

    /// <summary>
    /// Runs the worker until cancellation is requested.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="cancellationToken">The worker lifetime cancellation token.</param>
    /// <returns>A task that represents the worker lifetime.</returns>
    public static async Task RunAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = Build(args);
        try
        {
            await host.StartAsync(CancellationToken.None);
            await host.WaitForShutdownAsync(cancellationToken);
        }
        finally
        {
            host.Dispose();
        }
    }

    private static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.ConfigureDomainInjection();
        builder.Services.ConfigureApplicationInjection();
        builder.Services.ConfigureImageInfrastructureInjection(builder.Configuration);
        builder.Services.ConfigureDataProtection(
            builder.Configuration,
            builder.Environment);
        builder.Services.ConfigureInfrastructureInjection(builder.Configuration);
        builder.Services.ConfigureWorkerInjection(
            builder.Configuration,
            builder.Environment);

        return builder.Build();
    }
}
