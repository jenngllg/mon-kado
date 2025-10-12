using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Responses;

/// <summary>
/// Represents an error response returned by an API, providing details about the error condition.
/// </summary>
[ExcludeFromCodeCoverage]
public class ErrorResponse
{
    /// <summary>
    /// The HTTP status code associated with the error response.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// The title of the error response, typically indicating the type of error.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// The message providing additional details about the error condition.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The collection of validation errors.
    /// </summary>
    public IEnumerable<ValidationError>? ValidationErrors { get; set; }
}
