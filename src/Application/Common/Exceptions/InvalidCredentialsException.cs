namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
/// <summary>
/// Represents invalid credentials exception.
/// </summary>

public class InvalidCredentialsException()
    : Exception("The supplied credentials are invalid.")
{
}
