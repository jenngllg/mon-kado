namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies a durable administrative export operation.</summary>
public enum AdministrativeDataExportAction
{
    /// <summary>An administrator requested a new or reused archive.</summary>
    Requested,
    /// <summary>An authorized archive stream was released for download, not necessarily received.</summary>
    DownloadStarted
}
