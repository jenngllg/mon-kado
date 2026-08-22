namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an invalid or expired authentication session.
/// </summary>
public class InvalidAuthenticationSessionException()
    : Exception("The authentication session is invalid or expired.")
{
}
