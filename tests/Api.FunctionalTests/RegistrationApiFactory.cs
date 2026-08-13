using System.Collections.Concurrent;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class RegistrationApiFactory : WebApplicationFactory<Program>
{
    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=functional-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    public RecordingAccountRegistrationService RegistrationService { get; } = new();

    public IReadOnlyCollection<string> LogMessages => logProvider.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Local");
        builder.UseSetting("ConnectionStrings:PostgreSql", UnavailableConnectionString);
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("WebSecurity:AllowedOrigins:0", "http://localhost:5173");
        builder.ConfigureLogging(logging => logging.AddProvider(logProvider));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccountRegistrationService>();
            services.AddSingleton<IAccountRegistrationService>(RegistrationService);
        });
    }

    private readonly CapturingLoggerProvider logProvider = new();
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => messages.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(messages);
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            messages.Enqueue(formatter(state, exception));
        }
    }
}

public sealed class RecordingAccountRegistrationService : IAccountRegistrationService
{
    private readonly object sync = new();
    private int callCount;

    public int CallCount => Volatile.Read(ref callCount);

    public IReadOnlyList<RegistrationCall> Calls
    {
        get
        {
            lock (sync)
            {
                return calls.ToArray();
            }
        }
    }

    private readonly List<RegistrationCall> calls = [];

    public Task RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            calls.Add(new RegistrationCall(email, password, displayName));
        }

        Interlocked.Increment(ref callCount);
        return Task.CompletedTask;
    }
}

public sealed record RegistrationCall(string Email, string Password, string DisplayName);
