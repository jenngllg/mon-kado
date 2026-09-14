namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates that a verified authenticator and an active session are required.</summary>
public class TwoFactorAccessDeniedException() : Exception("A verified authenticator and an active session are required.");
