namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Classifies export failures without storing sensitive diagnostic text.</summary>
public enum PersonalDataExportFailure
{
    /// <summary>A technical generation failure exhausted its retries.</summary>
    GenerationFailed,
    /// <summary>The complete archive would exceed its configured size limit.</summary>
    TooLarge
}
