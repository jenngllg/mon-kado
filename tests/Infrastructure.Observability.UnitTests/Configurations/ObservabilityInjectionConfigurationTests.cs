using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Configurations;

public class ObservabilityInjectionConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureObservabilityInjection_WhenConfigured_UsesOneProcessCounterAndSafeConsole(bool configured)
    {
        // Arrange
        var values = configured
            ? new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "false",
                ["Observability:Interval"] = "00:00:45",
                ["Observability:Service"] = "untrusted",
                ["Observability:Version"] = "0123456789abcdef0123456789abcdef01234567"
            }
            : [];
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        var clock = new ControlledTimeProvider();
        services.AddSingleton<TimeProvider>(clock);

        // Act
        var registered = services.ConfigureObservabilityInjection(
            configuration,
            "worker",
            ["TEST_CODE"]);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var telemetry = provider.GetRequiredService<IApplicationTelemetry>();
        var options = provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        // Assert
        Assert.Same(
            services,
            registered);
        Assert.Same(
            clock,
            provider.GetRequiredService<TimeProvider>());
        Assert.Same(
            telemetry,
            scope.ServiceProvider.GetRequiredService<IApplicationTelemetry>());
        Assert.Same(
            telemetry,
            provider.GetRequiredService<ITelemetrySnapshotSource>());
        Assert.Equal(
            "worker",
            options.Service);
        Assert.Equal(
            TimeSpan.FromSeconds(configured ? 45 : 30),
            options.Interval);
        Assert.Equal(
            configured ? "0123456789abcdef0123456789abcdef01234567" : "local",
            options.Version);
        Assert.IsType<FileTelemetrySnapshotStore>(provider.GetRequiredService<ITelemetrySnapshotStore>());
        Assert.IsType<TelemetrySnapshotWorker>(Assert.Single(provider.GetServices<IHostedService>()));
        Assert.IsType<ConsoleLoggerProvider>(Assert.Single(provider.GetServices<ILoggerProvider>()));
        Assert.Equal(
            SafeJsonConsoleFormatter.FormatterName,
            provider.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        Assert.Contains(
            provider.GetServices<ConsoleFormatter>(),
            formatter => formatter is SafeJsonConsoleFormatter);
        Assert.Equal(
            "TEST_CODE",
            provider.GetRequiredService<ILogPropertyFilter>().Filter(
                "ErrorCode",
                "TEST_CODE"));
    }

    [Fact]
    public void ConfigureObservabilityInjection_WhenInvalid_FailsBeforeRegisteringServices()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<OptionsValidationException>(() => services.ConfigureObservabilityInjection(
            configuration,
            "unknown",
            []));

        // Assert
        Assert.Equal(
            "Observability",
            exception.OptionsName);
        Assert.Empty(services);
    }
}
