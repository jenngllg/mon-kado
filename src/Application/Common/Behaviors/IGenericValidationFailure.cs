namespace JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;

public interface IGenericValidationFailure
{
    Exception CreateValidationException();
}
