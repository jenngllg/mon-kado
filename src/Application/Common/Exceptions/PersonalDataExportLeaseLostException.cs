namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Fences a stale worker without exposing its lease token.</summary>
public class PersonalDataExportLeaseLostException() : Exception("The personal data export generation lease is no longer valid.");
