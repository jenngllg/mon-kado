namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Aborts generation rather than publishing a truncated archive.</summary>
public class PersonalDataExportTooLargeException() : Exception("The complete personal data export exceeds the configured size limit.");
