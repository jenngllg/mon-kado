using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Models;
/// <summary>
/// Represents validation error.
/// </summary>
/// <param name="propertyName">The property name.</param>
/// <param name="errorMessage">The error message.</param>

[ExcludeFromCodeCoverage]
public class ValidationError(
    string? propertyName,
    string? errorMessage)
{
    /// <summary>
    /// Gets property name.
    /// </summary>
    public string? PropertyName { get; } = propertyName;
    /// <summary>
    /// Gets error message.
    /// </summary>

    public string? ErrorMessage { get; } = errorMessage;
}
