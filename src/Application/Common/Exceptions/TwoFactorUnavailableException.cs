namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates unavailable second-factor security material without exposing protected payloads.</summary>
public class TwoFactorUnavailableException() : Exception("The second-factor security material is unavailable.");
