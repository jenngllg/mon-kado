using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Streams;

using Microsoft.Extensions.Options;

using System.Globalization;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Services;

/// <summary>Stores private immutable archives with bounded cross-process locking and orphan reconciliation.</summary>
public class LocalPersonalDataExportStore : IPersonalDataExportStore
{
    private const int BufferSize = 64 * 1024;
    private const int LockStripeMask = 63;
    private const string ArchiveExtension = ".zip";
    private const string TemporaryExtension = ".tmp";
    private const string ManifestExtension = ".manifest.tmp";
    private readonly string _storagePath;
    private readonly TimeProvider _timeProvider;
    private string? _reconciliationCursor;
    /// <summary>Initializes the private volume adapter and its best-effort bounded reconciliation cursor.</summary>
    /// <param name="options">The validated archive root configuration.</param>
    /// <param name="timeProvider">The UTC file-age clock.</param>
    public LocalPersonalDataExportStore(
        IOptions<PersonalDataExportStorageOptions> options,
        TimeProvider timeProvider)
    {
        var path = options.Value.StoragePath;
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _storagePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureNoLinkedAncestors(_storagePath);
            CreatePrivateDirectory(_storagePath);
            var locksPath = Path.Combine(
                _storagePath,
                ".locks");
            EnsureNoLinkedAncestors(locksPath);
            CreatePrivateDirectory(locksPath);
            var probePath = Path.Combine(
                _storagePath,
                $".probe-{Guid.CreateVersion7():N}");
            await using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await probe.WriteAsync(
                new byte[] { 1 },
                cancellationToken);
            await probe.FlushAsync(cancellationToken);
            probe.Position = 0;
            var buffer = new byte[1];
            await probe.ReadExactlyAsync(
                buffer,
                cancellationToken);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <inheritdoc/>
    public async Task<long> WriteAsync(
        Guid exportId,
        Guid archiveId,
        Func<PersonalDataExportWriteContext, CancellationToken, Task> writeAsync,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureAvailable();
            using var lease = TryAcquireLock(exportId);

            if (lease is null)
                throw new PersonalDataExportStorageUnavailableException();
            var directory = GetDirectory(exportId);
            EnsureNoLinkedAncestors(directory);
            CreatePrivateDirectory(directory);
            var temporaryPath = GetAttemptPath(
                exportId,
                archiveId,
                TemporaryExtension);
            var manifestPath = GetAttemptPath(
                exportId,
                archiveId,
                ManifestExtension);
            long length;
            await using (var file = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new SizeLimitedWriteStream(
                file,
                maximumBytes))
            await using (var manifest = new FileStream(
                manifestPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                File.SetLastWriteTimeUtc(
                    temporaryPath,
                    _timeProvider
                        .GetUtcNow()
                        .UtcDateTime);
                await writeAsync(
                    new PersonalDataExportWriteContext
                    {
                        Archive = destination,
                        ImageManifest = manifest
                    },
                    cancellationToken);
                await destination.FlushAsync(cancellationToken);
                length = destination.BytesWritten;
            }

            var archivePath = GetAttemptPath(
                exportId,
                archiveId,
                ArchiveExtension);
            File.Move(
                temporaryPath,
                archivePath,
                overwrite: false);
            File.SetLastWriteTimeUtc(
                archivePath,
                _timeProvider
                    .GetUtcNow()
                    .UtcDateTime);

            return length;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenReadAsync(
        Guid exportId,
        Guid archiveId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureAvailable();
            var directory = GetDirectory(exportId);
            EnsureNoLinkedAncestors(directory);
            var path = GetAttemptPath(
                exportId,
                archiveId,
                ArchiveExtension);
            EnsureRegularFile(path);
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {

            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {

            return Task.FromResult<Stream?>(null);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureAvailable();
            using var lease = TryAcquireLock(exportId);

            if (lease is null)
                return Task.FromResult(false);
            var directory = GetDirectory(exportId);
            EnsureNoLinkedAncestors(directory);

            if (!Directory.Exists(directory))
                return Task.FromResult(true);
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryParseAttempt(
                    path,
                    out _) || IsLinked(path))
                    continue;
                File.Delete(path);
            }

            if (!Directory
                .EnumerateFileSystemEntries(directory)
                .Any())
                Directory.Delete(directory);

            return Task.FromResult(true);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <inheritdoc/>
    public async Task ReconcileAsync(
        DateTime cutoff,
        int batchSize,
        Func<Guid, Guid, CancellationToken, Task<bool>> isReferencedAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureAvailable();
            var candidates = GetReconciliationCandidates(
                cutoff,
                batchSize,
                Volatile.Read(ref _reconciliationCursor),
                cancellationToken);

            if (candidates.Length == 0)
            {
                Volatile.Write(
                    ref _reconciliationCursor,
                    null);

                return;
            }

            foreach (var path in candidates)
            {
                await ReconcileCandidateAsync(
                    path,
                    cutoff,
                    isReferencedAsync,
                    cancellationToken);
                Volatile.Write(
                    ref _reconciliationCursor,
                    path);
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            Volatile.Write(
                ref _reconciliationCursor,
                null);

            throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <summary>Selects the next bounded batch without buffering all volume filenames or repeatedly revisiting protected archives.</summary>
    /// <param name="cutoff">The inclusive age cutoff.</param>
    /// <param name="batchSize">The maximum retained candidates.</param>
    /// <param name="cursor">The previous lexicographic position, when present.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>At most one batch in deterministic order.</returns>
    private string[] GetReconciliationCandidates(
        DateTime cutoff,
        int batchSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var candidates = new PriorityQueue<string, string>(Comparer<string>.Create((
                    left,
                    right) => string.CompareOrdinal(
                    right,
                    left)));
        foreach (var directory in Directory.EnumerateDirectories(_storagePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(
                Path.GetFileName(directory),
                "N",
                out _) || IsLinked(directory))
                continue;

            if (!Directory
                .EnumerateFileSystemEntries(directory)
                .Any() && Directory.GetCreationTimeUtc(directory) <= cutoff)
                AddCandidate(
                    candidates,
                    directory,
                    cursor,
                    batchSize);
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryParseAttempt(
                    path,
                    out _) && !IsLinked(path) && File.GetLastWriteTimeUtc(path) <= cutoff)
                    AddCandidate(
                        candidates,
                        path,
                        cursor,
                        batchSize);
            }
        }

        return candidates.UnorderedItems
            .Select(item => item.Element)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Keeps only the smallest next candidates using memory proportional to the configured batch size.</summary>
    /// <param name="candidates">The bounded max-heap.</param>
    /// <param name="path">The known generated candidate.</param>
    /// <param name="cursor">The previous position.</param>
    /// <param name="batchSize">The configured bound.</param>
    private static void AddCandidate(
        PriorityQueue<string, string> candidates,
        string path,
        string? cursor,
        int batchSize)
    {

        if (string.CompareOrdinal(
            path,
            cursor) <= 0)
            return;
        candidates.Enqueue(
            path,
            path);

        if (candidates.Count > batchSize)
            candidates.Dequeue();
    }

    /// <summary>Rechecks one selected candidate under the same cross-process exclusion as writes.</summary>
    /// <param name="path">The candidate selected from the private volume.</param>
    /// <param name="cutoff">The inclusive age cutoff.</param>
    /// <param name="isReferencedAsync">The durable reference check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the idempotent check.</returns>
    private async Task ReconcileCandidateAsync(
        string path,
        DateTime cutoff,
        Func<Guid, Guid, CancellationToken, Task<bool>> isReferencedAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directoryName = Path
            .GetRelativePath(
            _storagePath,
            path)
            .Split(Path.DirectorySeparatorChar)[0];
        var exportId = Guid.ParseExact(
            directoryName,
            "N");
        using var lease = TryAcquireLock(exportId);

        if (lease is null)
            return;
        var directory = Path.Combine(
            _storagePath,
            directoryName);
        EnsureNoLinkedAncestors(directory);

        if (path != directory && File.Exists(path) && !IsLinked(path) && File.GetLastWriteTimeUtc(path) <= cutoff)
        {
            _ = TryParseAttempt(
                path,
                out var archiveId);
            var referenced = await isReferencedAsync(
                exportId,
                archiveId,
                cancellationToken);

            if (!referenced)
                File.Delete(path);
        }

        if (Directory.Exists(directory) && !Directory
            .EnumerateFileSystemEntries(directory)
            .Any() && Directory.GetCreationTimeUtc(directory) <= cutoff)
            Directory.Delete(directory);
    }

    /// <summary>Acquires a bounded shared lock stripe without removing lock inodes used by other processes.</summary>
    /// <param name="exportId">The export identifier used to select a fixed stripe.</param>
    /// <returns>The owned lock, or null when another process holds it.</returns>
    private FileStream? TryAcquireLock(Guid exportId)
    {
        var stripe = exportId.ToByteArray()[15] & LockStripeMask;
        var path = Path.Combine(
            _storagePath,
            ".locks",
            stripe.ToString(
                "D2",
                CultureInfo.InvariantCulture) + ".lock");
        EnsureRegularFile(path);
        try
        {

            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {

            return null;
        }
    }

    /// <summary>Checks the mounted root without recreating an unavailable volume during a request.</summary>
    private void EnsureAvailable()
    {
        EnsureNoLinkedAncestors(_storagePath);

        if (!Directory.Exists(_storagePath))
            throw new PersonalDataExportStorageUnavailableException();
    }

    /// <summary>Builds one direct child directory exclusively from a generated identifier.</summary>
    /// <param name="exportId">The export identifier.</param>
    /// <returns>The private export directory.</returns>
    private string GetDirectory(Guid exportId)
    {

        return Path.Combine(
            _storagePath,
            exportId.ToString("N"));
    }

    /// <summary>Builds a known immutable attempt filename without accepting a client path.</summary>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="archiveId">The attempt identifier.</param>
    /// <param name="extension">The internal file kind.</param>
    /// <returns>The private attempt path.</returns>
    private string GetAttemptPath(
        Guid exportId,
        Guid archiveId,
        string extension)
    {

        return Path.Combine(
            GetDirectory(exportId),
            archiveId.ToString("N") + extension);
    }

    /// <summary>Recognizes only the filenames generated by this store.</summary>
    /// <param name="path">The candidate local file.</param>
    /// <param name="archiveId">The recognized attempt identifier.</param>
    /// <returns>Whether the name belongs to this store.</returns>
    private static bool TryParseAttempt(
        string path,
        out Guid archiveId)
    {
        var name = Path.GetFileName(path);
        archiveId = Guid.Empty;

        if (name.Length < 32)
            return false;
        var extension = name[32..];

        return extension is ArchiveExtension or TemporaryExtension or ManifestExtension && Guid.TryParseExact(
            name.AsSpan(
                0,
                32),
            "N",
            out archiveId);
    }

    /// <summary>Rejects filesystem links and directories when opening a regular file.</summary>
    /// <param name="path">The intended regular file.</param>
    private static void EnsureRegularFile(string path)
    {
        // All callers supply an absolute, generated file path below the validated storage root.
        EnsureNoLinkedAncestors(Path.GetDirectoryName(path)!);

        if (IsLinked(path) || Directory.Exists(path))
            throw new PersonalDataExportStorageUnavailableException();
    }

    /// <summary>Checks every ancestor so a replaced directory cannot redirect private storage.</summary>
    /// <param name="path">The intended directory.</param>
    private static void EnsureNoLinkedAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {

            if (IsLinked(directory.FullName))
                throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <summary>Detects existing and dangling links without opening their targets.</summary>
    /// <param name="path">The file or directory candidate.</param>
    /// <returns>Whether the candidate is a link or reparse point.</returns>
    private static bool IsLinked(string path)
    {
        try
        {

            return File
                .GetAttributes(path)
                .HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {

            return false;
        }
    }

    /// <summary>Creates private directories under the process identity on Unix hosts.</summary>
    /// <param name="path">The validated directory.</param>
    private static void CreatePrivateDirectory(string path)
    {

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);

            return;
        }

        Directory.CreateDirectory(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Recognizes filesystem outages whose messages must never leave the storage boundary.</summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns>Whether this is a filesystem failure.</returns>
    private static bool IsStorageFailure(Exception exception)
    {

        return exception is IOException or UnauthorizedAccessException;
    }
}
