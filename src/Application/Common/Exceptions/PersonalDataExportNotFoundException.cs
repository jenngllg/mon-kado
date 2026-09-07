namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents an unknown, expired or inaccessible export.</summary>
public class PersonalDataExportNotFoundException() : Exception("The requested personal data export was not found.");
