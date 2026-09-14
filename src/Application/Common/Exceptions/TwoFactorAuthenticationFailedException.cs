namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates that the second-factor proof is invalid, expired or already consumed.</summary>
public class TwoFactorAuthenticationFailedException() : Exception("The second-factor proof is invalid, expired or already consumed.");
