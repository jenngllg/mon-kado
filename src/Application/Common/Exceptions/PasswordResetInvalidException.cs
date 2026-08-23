namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an invalid or expired password reset link.
/// </summary>
public class PasswordResetInvalidException()
    : Exception("The password reset link is invalid or expired.");
