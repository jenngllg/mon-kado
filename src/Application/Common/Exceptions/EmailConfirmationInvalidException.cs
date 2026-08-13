namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

public sealed class EmailConfirmationInvalidException()
    : Exception("The email confirmation link is invalid or expired.")
{
}
