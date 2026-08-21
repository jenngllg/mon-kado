using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Api.Logging;
/// <summary>
/// Represents api log messages.
/// </summary>

public static partial class ApiLogMessages
{
    /// <summary>
    /// Executes the expected http error operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="errorCode">The error code.</param>
    [LoggerMessage(
        EventId = LogEventIds.ExpectedHttpError,
        Level = LogLevel.Error,
        Message = "HTTP request failed with status {StatusCode} and error code {ErrorCode}.")]
    public static partial void ExpectedHttpError(
        ILogger logger,
        int statusCode,
        string errorCode);
    /// <summary>
    /// Executes the dependency unavailable operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exceptionType">The exception type.</param>
    /// <param name="exception">The exception.</param>

    [LoggerMessage(
        EventId = LogEventIds.DependencyUnavailable,
        Level = LogLevel.Error,
        Message = "A request dependency was unavailable. Exception type: {ExceptionType}.")]
    public static partial void DependencyUnavailable(
        ILogger logger,
        string exceptionType,
        Exception exception);
    /// <summary>
    /// Executes the unhandled exception operation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception.</param>

    [LoggerMessage(
        EventId = LogEventIds.UnhandledException,
        Level = LogLevel.Error,
        Message = "An unhandled exception occurred while processing a request.")]
    public static partial void UnhandledException(
        ILogger logger,
        Exception exception);
}
