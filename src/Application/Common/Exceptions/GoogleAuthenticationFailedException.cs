namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents a validated Google authentication that cannot resolve to a MonKado account.
/// </summary>
public class GoogleAuthenticationFailedException()
    : Exception("Google authentication could not be completed.");
