namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
/// <summary>
/// Represents email confirmation invalid exception.
/// </summary>

public class EmailConfirmationInvalidException()
    : Exception("The email confirmation link is invalid or expired.")
{
}
