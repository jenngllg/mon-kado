using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

/// <summary>
/// Stores immutable normalized gift images on a shared local filesystem volume.
/// </summary>
public class LocalGiftImageStore : IGiftImageStore
{
    private const string ImageExtension = ".webp";
    private const string PendingExtension = ".pending";

    private readonly string _storagePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalGiftImageStore" /> class.
    /// </summary>
    /// <param name="options">The local image storage options.</param>
    public LocalGiftImageStore(IOptions<GiftImageStorageOptions> options)
    {
        var storagePath = options.Value.StoragePath;
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        _storagePath = Path.GetFullPath(storagePath);
    }

    /// <inheritdoc />
    public async Task WritePendingAsync(
        Guid imageId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var imagePath = GetImagePath(imageId);
        var pendingPath = GetPendingPath(imageId);
        var directoryPath = GetDirectoryPath(imageId);
        var temporaryImagePath = imagePath + $".{Guid.NewGuid():N}.tmp";
        var temporaryPendingPath = pendingPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllBytesAsync(
                temporaryPendingPath,
                [],
                cancellationToken);
            File.Move(
                temporaryPendingPath,
                pendingPath,
                overwrite: false);
            await File.WriteAllBytesAsync(
                temporaryImagePath,
                content,
                cancellationToken);
            File.Move(
                temporaryImagePath,
                imagePath,
                overwrite: false);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(temporaryImagePath);
            DeleteIfExists(temporaryPendingPath);

            throw;
        }
        catch (Exception exception)
        {
            DeleteIfExists(temporaryImagePath);
            DeleteIfExists(temporaryPendingPath);

            if (!IsStorageFailure(exception))
                throw;

            throw new GiftImageStorageUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public Task MarkCommittedAsync(
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            DeleteIfExists(GetPendingPath(imageId));

            return Task.CompletedTask;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new GiftImageStorageUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Stream stream = new FileStream(
                GetImagePath(imageId),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
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
            throw new GiftImageStorageUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            DeleteIfExists(GetImagePath(imageId));
            DeleteIfExists(GetPendingPath(imageId));

            return Task.CompletedTask;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new GiftImageStorageUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<PendingGiftImage>> GetPendingAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Linux can report a file used as a directory as DirectoryNotFoundException.
            if (File.Exists(_storagePath))
                throw new IOException("The image storage path is not a directory.");

            var pendingImages = Directory
                .EnumerateFiles(
                    _storagePath,
                    $"*{PendingExtension}",
                    SearchOption.AllDirectories)
                .Select(path => CreatePendingGiftImage(
                    path,
                    cancellationToken))
                .OfType<PendingGiftImage>()
                .Where(image => image.CreatedAt <= cutoff)
                .OrderBy(image => image.CreatedAt)
                .Take(batchSize)
                .ToArray();

            return Task.FromResult<IReadOnlyCollection<PendingGiftImage>>(pendingImages);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<IReadOnlyCollection<PendingGiftImage>>([]);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new GiftImageStorageUnavailableException(exception);
        }
    }

    /// <summary>
    /// Creates pending-image metadata from a marker whose name is an image identifier.
    /// </summary>
    /// <param name="path">The marker path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pending image, or <see langword="null" /> for an unrelated marker.</returns>
    private static PendingGiftImage? CreatePendingGiftImage(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileName = Path.GetFileNameWithoutExtension(path);

        if (!Guid.TryParseExact(
                fileName,
                "N",
                out var imageId))
            return null;

        return new PendingGiftImage(
            imageId,
            File.GetLastWriteTimeUtc(path));
    }

    /// <summary>
    /// Gets the immutable image path derived only from its identifier.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <returns>The absolute image path.</returns>
    private string GetImagePath(Guid imageId)
    {
        var value = imageId.ToString("N");

        return Path.Combine(
            GetDirectoryPath(imageId),
            value + ImageExtension);
    }

    /// <summary>
    /// Gets the sharded storage directory derived only from an image identifier.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <returns>The absolute sharded directory path.</returns>
    private string GetDirectoryPath(Guid imageId)
    {
        var value = imageId.ToString("N");

        return Path.Combine(
            _storagePath,
            value[..2],
            value.Substring(
                2,
                2));
    }

    /// <summary>
    /// Gets the pending-marker path derived only from an image identifier.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <returns>The absolute pending-marker path.</returns>
    private string GetPendingPath(Guid imageId)
    {
        return Path.ChangeExtension(
            GetImagePath(imageId),
            PendingExtension);
    }

    /// <summary>
    /// Determines whether an exception represents unavailable local storage.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true" /> for storage access failures.</returns>
    private static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    /// <summary>
    /// Deletes a file idempotently, including when its parent directory is absent.
    /// </summary>
    /// <param name="path">The exact file path.</param>
    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
