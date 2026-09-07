using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.Results;

/// <summary>Streams an attachment while preventing filesystem paths from escaping through read failures.</summary>
public class PersonalDataExportFileResult : FileStreamResult
{
    private readonly Guid _exportId;
    /// <summary>Creates an owned ZIP attachment with an identifier-only filename.</summary>
    /// <param name="download">The authorized caller-owned archive stream.</param>
    public PersonalDataExportFileResult(PersonalDataExportDownload download) : base(
        download.Content,
        "application/zip")
    {
        _exportId = download.ExportId;
        FileDownloadName = $"monkado-data-{download.ExportId:D}.zip";
    }

    /// <inheritdoc/>
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        try
        {
            await base.ExecuteResultAsync(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PersonalDataExportStorageUnavailableException)
        {

            if (!context.HttpContext.Response.HasStarted)
                throw new PersonalDataExportStorageUnavailableException();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<PersonalDataExportFileResult>>();
            PersonalDataExportLogMessages.DownloadFailed(
                logger,
                _exportId);
            context.HttpContext.Abort();
        }
    }
}
