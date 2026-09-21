using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

using Microsoft.Extensions.Options;

using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

/// <summary>Writes bounded snapshots atomically in a dedicated service-owned directory.</summary>
public class FileTelemetrySnapshotStore(IOptions<ObservabilityOptions> options) : ITelemetrySnapshotStore
{
    private const int MaximumBytes = 65536;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <inheritdoc />
    public async Task WriteAsync(
        ApplicationTelemetrySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            _jsonOptions);

        if (data.Length > MaximumBytes)
            throw new InvalidOperationException("Telemetry snapshot exceeds the size limit.");
        var directory = options.Value.Directory;
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(
            directory,
            "snapshot.json");
        var temporary = Path.Combine(
            directory,
            "snapshot.new");
        RejectLink(directory);
        RejectLink(destination);
        RejectLink(temporary);
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                PrivateFileMode | UnixFileMode.UserExecute);
            streamOptions.UnixCreateMode = PrivateFileMode;
        }
        await using (var stream = new FileStream(
            temporary,
            streamOptions))
        {
            await stream.WriteAsync(
                data,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                temporary,
                PrivateFileMode);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(
            temporary,
            destination,
            overwrite: true);
    }

    /// <summary>Rejects links before accessing fixed snapshot paths in a private directory.</summary>
    /// <param name="path">The fixed local path.</param>
    /// <exception cref="IOException">The path is a symbolic link.</exception>
    private static void RejectLink(string path)
    {

        if (new FileInfo(path).LinkTarget is not null)
            throw new IOException("Telemetry paths must not be symbolic links.");
    }
}
