namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents the durable per-member export quota being exhausted.</summary>
public class PersonalDataExportRateLimitException() : Exception("Too many personal data export requests. Try again later.");
