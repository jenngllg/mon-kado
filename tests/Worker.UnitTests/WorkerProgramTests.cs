using JennGllg.Fr.MonKado.Back.Worker.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class WorkerProgramTests
{
    [Fact]
    public async Task Main_WhenConnectionStringIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var args = new[]
        {
            "--environment=Staging",
            "--ConnectionStrings:PostgreSql="
        };

        // Act
#pragma warning disable xUnit1051 // The process entry point does not accept a cancellation token.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Program.Main(args));
#pragma warning restore xUnit1051

        // Assert
        Assert.Contains(
            "Connection string 'PostgreSql' is required",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationIsRequested_PreservesCancellation()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        source.Cancel();
        var args = new[]
        {
            "--environment=Staging",
            "--ConnectionStrings:PostgreSql=Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=test",
            $"--{AuthenticationEmailOptions.SectionName}:Provider={AuthenticationEmailOptions.DisabledProvider}"
        };

        // Act
        Task action() => Program.RunAsync(
            args,
            source.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationOccursAfterStartup_Completes()
    {
        // Arrange
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var args = new[]
        {
            "--environment=Staging",
            "--ConnectionStrings:PostgreSql=Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=test",
            $"--{AuthenticationEmailOptions.SectionName}:Provider={AuthenticationEmailOptions.DisabledProvider}"
        };

        // Act
        var exception = await Record.ExceptionAsync(() => Program.RunAsync(
            args,
            source.Token));

        // Assert
        Assert.Null(exception);
    }
}
