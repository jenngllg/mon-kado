namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates that second-factor verification is temporarily rate limited.</summary>
public class TwoFactorRateLimitException() : Exception("Second-factor verification is temporarily rate limited.");
