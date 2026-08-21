using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
/// <summary>
/// Represents request validation exception.
/// </summary>
/// <param name="validationErrors">The validation errors.</param>

public class RequestValidationException(IEnumerable<ValidationError> validationErrors)
    : Exception("One or more request fields are invalid.")
{
    /// <summary>
    /// Gets validation errors.
    /// </summary>
    public IEnumerable<ValidationError> ValidationErrors { get; } = validationErrors;
}
