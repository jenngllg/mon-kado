using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Services;

public class FileTelemetrySnapshotStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "monkado-telemetry-tests-" + Guid.CreateVersion7().ToString("N"));
    private readonly FileTelemetrySnapshotStore _store;

    public FileTelemetrySnapshotStoreTests()
    {
        _store = new FileTelemetrySnapshotStore(Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions
        {
            Directory = _directory
        }));
    }

    [Fact]
    public async Task WriteAsync_WhenReplacingSnapshot_ProducesVersionedJsonWithoutTemporaryFile()
    {
        // Arrange
        var snapshot = new ApplicationTelemetrySnapshot
        {
            Service = "api",
            BootId = new string('a', 32),
            CreatedAt = new DateTime(
                2026,
                9,
                21,
                12,
                0,
                0,
                DateTimeKind.Utc)
        };
        await _store.WriteAsync(
            snapshot,
            TestContext.Current.CancellationToken);

        // Act
        await _store.WriteAsync(
            snapshot,
            TestContext.Current.CancellationToken);

        // Assert
        var path = Path.Combine(
            _directory,
            "snapshot.json");
        var json = await File.ReadAllTextAsync(
            path,
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("api", document.RootElement.GetProperty("service").GetString());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task WriteAsync_WhenOversized_DoesNotCreateFiles()
    {
        // Arrange
        var snapshot = new ApplicationTelemetrySnapshot { BootId = new string('a', 65536) };

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.WriteAsync(
            snapshot,
            TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task WriteAsync_WhenCanceled_PreservesLastSnapshotAndCanRecover()
    {
        // Arrange
        var original = new ApplicationTelemetrySnapshot { Service = "api" };
        var replacement = new ApplicationTelemetrySnapshot { Service = "worker" };
        await _store.WriteAsync(
            original,
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var destination = Path.Combine(
            _directory,
            "snapshot.json");
        var before = await File.ReadAllBytesAsync(
            destination,
            TestContext.Current.CancellationToken);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.WriteAsync(
            replacement,
            cancellation.Token));

        // Assert
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(
                destination,
                TestContext.Current.CancellationToken));
        await _store.WriteAsync(
            replacement,
            TestContext.Current.CancellationToken);
        Assert.Single(Directory.GetFiles(_directory));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(
            destination,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            "worker",
            json.RootElement.GetProperty("service").GetString());
    }

    [Theory]
    [InlineData("snapshot.json")]
    [InlineData("snapshot.new")]
    public async Task WriteAsync_WhenLinuxPathIsSymbolicLink_DoesNotOverwriteTarget(string name)
    {
        // Arrange

        if (OperatingSystem.IsWindows())
            return;
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(
            _directory,
            "preserved.txt");
        await File.WriteAllTextAsync(
            target,
            "original",
            TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(
            Path.Combine(
                _directory,
                name),
            target);

        // Act
        await Assert.ThrowsAsync<IOException>(() => _store.WriteAsync(
            new ApplicationTelemetrySnapshot(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(
                target,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteAsync_WhenLinuxDirectoryExists_EnforcesPrivateModes()
    {
        // Arrange

        if (OperatingSystem.IsWindows())
            return;
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(
            _directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);

        // Act
        await _store.WriteAsync(
            new ApplicationTelemetrySnapshot(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_directory));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(
                _directory,
                "snapshot.json")));
    }

    public void Dispose()
    {

        if (Directory.Exists(_directory))
            Directory.Delete(
                _directory,
                recursive: true);
        GC.SuppressFinalize(this);
    }
}
