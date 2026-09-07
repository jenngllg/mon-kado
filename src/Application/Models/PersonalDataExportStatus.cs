namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Defines the externally observable export lifecycle.</summary>
public enum PersonalDataExportStatus
{
    /// <summary>The request is waiting for a generation attempt.</summary>
    Queued,
    /// <summary>A leased worker is preparing the complete archive.</summary>
    Processing,
    /// <summary>The complete archive is available until its absolute expiry.</summary>
    Ready,
    /// <summary>Generation exhausted its attempts or encountered a permanent limit.</summary>
    Failed,
    /// <summary>The archive is no longer downloadable and awaits cleanup.</summary>
    Expired
}
