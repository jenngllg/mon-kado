using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common.Behaviors;

internal class ValidationErrorComparer : IEqualityComparer<ValidationError>
{
    public static ValidationErrorComparer Instance { get; } = new();

    public bool Equals(
        ValidationError? left,
        ValidationError? right)
    {
        return left?.PropertyName == right?.PropertyName &&
            left?.ErrorMessage == right?.ErrorMessage;
    }

    public int GetHashCode(ValidationError value)
    {
        return HashCode.Combine(
            value.PropertyName,
            value.ErrorMessage);
    }
}
