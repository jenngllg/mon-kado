namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Reports an export-storage outage without retaining paths or raw filesystem exceptions.</summary>
public class PersonalDataExportStorageUnavailableException() : DependencyUnavailableException(
    "Personal data export storage",
    null);
