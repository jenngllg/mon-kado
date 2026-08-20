namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

public sealed class EmailNotConfirmedException()
    : Exception("The account email address is not confirmed.")
{
}
