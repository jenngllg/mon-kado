namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents an archive that is not available for download yet.</summary>
public class PersonalDataExportNotReadyException() : Exception("The personal data export is not ready for download.");
