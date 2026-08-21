using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RegistrationApiFactory : WebApplicationFactory<Program>
{
    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=functional-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    public RecordingAccountRegistrationService RegistrationService { get; } = new();

    public RecordingEmailConfirmationService EmailConfirmationService { get; } = new();

    public RecordingAccountSessionService SessionService { get; } = new();

    public IReadOnlyCollection<string> LogMessages => _logProvider.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Local");
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            UnavailableConnectionString);
        builder.UseSetting(
            "AllowedHosts",
            "localhost");
        builder.UseSetting(
            "WebSecurity:AllowedOrigins:0",
            "http://localhost:5173");
        builder.ConfigureLogging(logging => logging.AddProvider(_logProvider));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccountRegistrationService>();
            services.AddSingleton<IAccountRegistrationService>(RegistrationService);
            services.RemoveAll<IEmailConfirmationService>();
            services.RemoveAll<IAccountSessionService>();
            services.AddSingleton<IAccountSessionService>(SessionService);
            services.AddSingleton<IEmailConfirmationService>(EmailConfirmationService);
        });
    }

    private readonly CapturingLoggerProvider _logProvider = new();
}
