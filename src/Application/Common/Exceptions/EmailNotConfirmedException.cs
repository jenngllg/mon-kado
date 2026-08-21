namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
/// <summary>
/// Represents email not confirmed exception.
/// </summary>

public class EmailNotConfirmedException()
    : Exception("The account email address is not confirmed.")
{
}
