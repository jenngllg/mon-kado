using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Responses;

/// <summary>
/// Represents a validation error that occurs when a property fails validation.
/// </summary>
/// <remarks>This class is typically used to encapsulate details about a validation failure.</remarks>
[ExcludeFromCodeCoverage]
public class ValidationError
{
    /// <summary>
    /// The name of the property that failed validation.
    /// </summary>
    public string PropertyName { get; set; }

    /// <summary>
    /// The error message associated with the validation failure.
    /// </summary>
    public string ErrorMessage { get; set; }
}
