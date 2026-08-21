using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Errors;
/// <summary>
/// Represents error response.
/// </summary>
/// <param name="statusCode">The status code.</param>
/// <param name="title">The title.</param>
/// <param name="message">The message.</param>
/// <param name="errorCode">The error code.</param>
/// <param name="validationErrors">The validation errors.</param>

[ExcludeFromCodeCoverage]
public class ErrorResponse(
    int statusCode,
    string? title,
    string? message,
    string? errorCode,
    IEnumerable<ValidationError>? validationErrors)
{
    /// <summary>
    /// Gets status code.
    /// </summary>
    public int StatusCode { get; } = statusCode;
    /// <summary>
    /// Gets title.
    /// </summary>

    public string? Title { get; } = title;
    /// <summary>
    /// Gets message.
    /// </summary>

    public string? Message { get; } = message;
    /// <summary>
    /// Gets error code.
    /// </summary>

    public string? ErrorCode { get; } = errorCode;
    /// <summary>
    /// Gets validation errors.
    /// </summary>

    public IEnumerable<ValidationError>? ValidationErrors { get; } = validationErrors;
}
