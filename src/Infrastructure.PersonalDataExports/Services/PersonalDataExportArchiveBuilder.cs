using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;

using Microsoft.Extensions.Options;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Services;

/// <summary>Streams a snapshot and verified immutable images into a size-bounded private ZIP.</summary>
/// <param name="snapshotReader">The authorized database snapshot reader.</param>
/// <param name="imageStore">The existing normalized image store.</param>
/// <param name="exportStore">The private atomic archive store.</param>
/// <param name="options">The validated generation limits.</param>
public class PersonalDataExportArchiveBuilder(
    IPersonalDataExportSnapshotReader snapshotReader,
    IGiftImageStore imageStore,
    IPersonalDataExportStore exportStore,
    IOptions<PersonalDataExportOptions> options) : IPersonalDataExportArchiveBuilder
{
    private const int BufferSize = 64 * 1024;
    private const string Readme = """
        MonKado personal data export - schema version 1

        data.json contains a UTF-8 JSON snapshot of the retained data associated with
        your account. Property names and enum values use camelCase. Dates are UTC.
        snapshotAt identifies the database snapshot, not the download time.

        Images are the current normalized WebP files, not the original uploads.
        imagePath values are relative to this archive. A null value means no image.
        No image URL, password, credential, token, security stamp or sharing secret
        is included. Reservations belonging to other people are not included.

        Only data still retained at snapshot time can be exported. Deleted records
        are not reconstructed. Raw operational logs and internal moderation files
        are not included in this automatic export; contact MonKado through the
        support contact in its privacy notice for a complementary access request.
        This archive is not an account backup and cannot be reimported into MonKado.

        Keep this archive private: it contains personal information.
    """;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    /// <inheritdoc/>
    public async Task<PersonalDataExportArchive> BuildAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var snapshotAt = default(DateTime);
        try
        {
            var length = await exportStore.WriteAsync(
                workItem.ExportId,
                workItem.LeaseId,
                async (
                    context,
                    token) =>
                {
                    using var archive = new ZipArchive(
                        context.Archive,
                        ZipArchiveMode.Create,
                        leaveOpen: true);
                    var data = archive.CreateEntry(
                        PersonalDataExportArchiveNames.Data,
                        CompressionLevel.Fastest);
                    await using (var destination = data.Open())
                    {
                        snapshotAt = await snapshotReader.WriteAsync(
                            workItem.MemberId,
                            destination,
                            context.ImageManifest,
                            token);
                    }

                    context.ImageManifest.Position = 0;
                    await foreach (var image in JsonSerializer.DeserializeAsyncEnumerable<PersonalDataExportImage>(
                        context.ImageManifest,
                        _jsonOptions,
                        token))
                    {

                        if (image is null)
                            throw new PersonalDataExportStorageUnavailableException();
                        await WriteImageAsync(
                            archive,
                            image,
                            token);
                    }

                    var readme = archive.CreateEntry(
                        PersonalDataExportArchiveNames.Readme,
                        CompressionLevel.Fastest);
                    await using (var readmeStream = readme.Open())
                    {
                        await readmeStream.WriteAsync(
                            Encoding.UTF8.GetBytes(Readme),
                            token);
                    }

                    token.ThrowIfCancellationRequested();
                },
                options.Value.MaximumArchiveBytes,
                cancellationToken);

            return new PersonalDataExportArchive
            {
                SnapshotAt = snapshotAt,
                SizeInBytes = length
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or GiftImageStorageUnavailableException)
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
    }

    /// <summary>Copies an immutable image and verifies the complete copied content before publication.</summary>
    /// <param name="archive">The unpublished archive.</param>
    /// <param name="image">The private image reference from the database snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the verified copy.</returns>
    private async Task WriteImageAsync(
        ZipArchive archive,
        PersonalDataExportImage image,
        CancellationToken cancellationToken)
    {
        await using var source = await imageStore.OpenReadAsync(
            image.ImageId,
            cancellationToken);

        if (source is null)
            throw new PersonalDataExportStorageUnavailableException();
        var entry = archive.CreateEntry(
            PersonalDataExportArchiveNames.GetImagePath(image.WishId),
            CompressionLevel.NoCompression);
        await using var destination = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(
            buffer,
            cancellationToken)) > 0)
        {
            hash.AppendData(
                buffer,
                0,
                bytesRead);
            await destination.WriteAsync(
                buffer.AsMemory(
                    0,
                    bytesRead),
                cancellationToken);
        }

        if (!CryptographicOperations.FixedTimeEquals(
            hash.GetHashAndReset(),
            image.ContentHash))
            throw new PersonalDataExportStorageUnavailableException();
    }
}
