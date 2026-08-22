using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
/// <summary>
/// Defines the contract for generic validation failure.
/// </summary>

public interface IGenericValidationFailure
{
    /// <summary>
    /// Executes the create validation exception operation.
    /// </summary>
    /// <param name="validationErrors">The aggregated validation errors.</param>
    /// <returns>The operation result.</returns>
    Exception CreateValidationException(IEnumerable<ValidationError> validationErrors);
}
