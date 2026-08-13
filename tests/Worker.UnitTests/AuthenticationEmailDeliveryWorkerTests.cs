using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Worker;
using JennGllg.Fr.MonKado.Back.Worker.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public sealed class AuthenticationEmailDeliveryWorkerTests
{
    private static readonly Uri FrontendOrigin = new("https://mon-kado.fr");

    [Fact]
    public async Task DisabledWorkerCompletesWithoutResolvingDispatcher()
    {
        await using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        AuthenticationEmailDeliveryWorker worker = CreateWorker(
            provider,
            new AuthenticationEmailOptions { Provider = AuthenticationEmailOptions.DisabledProvider });

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.ExecuteTask!.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task EnabledWorkerDispatchesAndStopsCleanlyDuringPollingDelay()
    {
        RecordingDispatcher dispatcher = new();
        await using ServiceProvider provider = CreateProvider(dispatcher);
        AuthenticationEmailDeliveryWorker worker = CreateWorker(
            provider,
            EnabledOptions());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await dispatcher.Called.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(FrontendOrigin, dispatcher.FrontendOrigin);
        Assert.Equal(20, dispatcher.BatchSize);
        Assert.Equal(TimeSpan.FromMinutes(2), dispatcher.LeaseDuration);
    }

    [Fact]
    public async Task SuccessfulIterationUsesNormalPollingDelay()
    {
        RecordingDispatcher dispatcher = new();
        await using ServiceProvider provider = CreateProvider(dispatcher);
        AuthenticationEmailDeliveryWorker worker = CreateWorker(provider, EnabledOptions());

        TimeSpan delay = await worker.DispatchOnceAsync(
            FrontendOrigin,
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    [Fact]
    public async Task FailedIterationUsesFailureDelay()
    {
        RecordingDispatcher dispatcher = new(new InvalidOperationException("Database unavailable."));
        await using ServiceProvider provider = CreateProvider(dispatcher);
        AuthenticationEmailDeliveryWorker worker = CreateWorker(provider, EnabledOptions());

        TimeSpan delay = await worker.DispatchOnceAsync(
            FrontendOrigin,
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(1), delay);
    }

    [Fact]
    public async Task IterationPreservesHostCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        RecordingDispatcher dispatcher = new(new OperationCanceledException(source.Token));
        await using ServiceProvider provider = CreateProvider(dispatcher);
        AuthenticationEmailDeliveryWorker worker = CreateWorker(provider, EnabledOptions());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.DispatchOnceAsync(FrontendOrigin, source.Token));
    }

    private static AuthenticationEmailOptions EnabledOptions() => new()
    {
        Provider = AuthenticationEmailOptions.GmailProvider,
        FrontendOrigin = FrontendOrigin.AbsoluteUri.TrimEnd('/')
    };

    private static ServiceProvider CreateProvider(IAuthenticationEmailDispatcher dispatcher)
    {
        ServiceCollection services = new();
        services.AddSingleton(dispatcher);
        services.AddSingleton<IAuthenticationEmailDispatcher>(dispatcher);
        return services.BuildServiceProvider();
    }

    private static AuthenticationEmailDeliveryWorker CreateWorker(
        ServiceProvider provider,
        AuthenticationEmailOptions options) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<AuthenticationEmailDeliveryWorker>.Instance);

    private sealed class RecordingDispatcher(Exception? exception = null) : IAuthenticationEmailDispatcher
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri? FrontendOrigin { get; private set; }

        public int BatchSize { get; private set; }

        public TimeSpan LeaseDuration { get; private set; }

        public Task<int> DispatchPendingAsync(
            Uri frontendOrigin,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            FrontendOrigin = frontendOrigin;
            BatchSize = batchSize;
            LeaseDuration = leaseDuration;
            Called.TrySetResult();
            return exception is null
                ? Task.FromResult(0)
                : Task.FromException<int>(exception);
        }
    }
}
