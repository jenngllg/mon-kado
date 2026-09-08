using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Shares archive lifetime, storage validation and failed-open disposal across authorized export routes.</summary>
/// <param name="store">The private archive store.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class PersonalDataExportArchiveReader(
    IPersonalDataExportStore store,
    TimeProvider timeProvider) : IPersonalDataExportArchiveReader
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDownload> OpenAsync(
        MemberDataExport dataExport,
        Func<CancellationToken, Task> authorizeReleaseAsync,
        CancellationToken cancellationToken)
    {
        var details = dataExport.GetDetails(timeProvider
                .GetUtcNow()
                .UtcDateTime);

        if (details.Status is PersonalDataExportStatus.Expired)
            throw new PersonalDataExportNotFoundException();

        if (details.Status is not PersonalDataExportStatus.Ready || dataExport.ArchiveId is not { } archiveId)
            throw new PersonalDataExportNotReadyException();
        var stream = await store.OpenReadAsync(
            dataExport.Id,
            archiveId,
            cancellationToken);

        if (stream is null)
            throw new PersonalDataExportStorageUnavailableException();
        PersonalDataExportDownload? download = null;
        try
        {

            if (stream.Length != dataExport.SizeInBytes)
                throw new PersonalDataExportStorageUnavailableException();
            await authorizeReleaseAsync(cancellationToken);

            if (dataExport.ExpiresAt.GetValueOrDefault() <= timeProvider
                .GetUtcNow()
                .UtcDateTime)
                throw new PersonalDataExportNotFoundException();
            download = new PersonalDataExportDownload
            {
                ExportId = dataExport.Id,
                Content = stream
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
        finally
        {

            if (download is null)
                await DisposeFailedDownloadAsync(stream);
        }

        return download;
    }

    /// <summary>Closes an untransferred stream without exposing filesystem paths.</summary>
    /// <param name="stream">The stream still owned by the reader.</param>
    /// <returns>A task representing disposal.</returns>
    private static async Task DisposeFailedDownloadAsync(Stream stream)
    {
        try
        {
            await stream.DisposeAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {

            throw new PersonalDataExportStorageUnavailableException();
        }
    }
}
