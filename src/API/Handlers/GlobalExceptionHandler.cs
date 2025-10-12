using FluentValidation;
using JennGllg.Fr.MonKado.Back.Domain.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Responses;
using Microsoft.AspNetCore.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.Handlers;

/// <summary>
/// Provides a mechanism for handling exceptions in an HTTP context.
/// </summary>
/// <remarks>This class implements the <see cref="IExceptionHandler"/> interface to handle exceptions that occur
/// during the processing of HTTP requests. It generates an appropriate error response based on the type of exception
/// and writes it to the HTTP response.</remarks>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    /// <summary>
    /// Attempts to handle the specified exception by generating an appropriate error response and writing it to the
    /// HTTP response.
    /// </summary>
    /// <remarks>This method writes an error response to the HTTP response stream and sets the appropriate
    /// status code. It is the caller's responsibility to ensure that the <paramref name="httpContext"/> and <paramref
    /// name="exception"/> parameters are not <see langword="null"/>.</remarks>
    /// <param name="httpContext">The <see cref="HttpContext"/> representing the current HTTP request and response.</param>
    /// <param name="exception">The <see cref="Exception"/> to handle. This cannot be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that resolves to <see langword="true"/> if the exception was successfully
    /// handled.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var errorResponse = CreateErrorResponse(exception);
        httpContext.Response.StatusCode = errorResponse.StatusCode;

        await httpContext
            .Response
            .WriteAsJsonAsync(errorResponse,
                cancellationToken);

        logger.LogError(exception, "An unhandled exception occurred : {Message}", exception.Message);

        return await ValueTask.FromResult(true); // Indicating that the exception was handled
    }

    #region Private methods

    /// <summary>
    /// Creates an <see cref="ErrorResponse"/> object based on the provided exception.
    /// </summary>
    /// <remarks>The method maps <see cref="ValidationException"/> to a 400 Bad Request status code and
    /// includes validation error details. For all other exceptions, it returns a 500 Internal Server Error with a
    /// generic error title.</remarks>
    /// <param name="exception">The exception to convert into an error response. Must not be <see langword="null"/>.</param>
    /// <returns>An <see cref="ErrorResponse"/> object containing details about the exception, including its type, message,  and
    /// an appropriate HTTP status code. For <see cref="ValidationException"/>, validation errors are included.</returns>
    private static ErrorResponse CreateErrorResponse(Exception exception)
    {
        var errorResponse = new ErrorResponse
        {
            Title = exception.GetType().Name,
            Message = exception.Message
        };

        switch (exception)
        {
            case ValidationException ex:
                errorResponse.StatusCode = StatusCodes.Status400BadRequest;
                errorResponse.ValidationErrors = ex.Errors.Select(x => new ValidationError
                {
                    PropertyName = x.PropertyName,
                    ErrorMessage = x.ErrorMessage
                });
                break;

            case NotFoundException ex:
                errorResponse.StatusCode = StatusCodes.Status404NotFound;
                break;

            default:
                errorResponse.StatusCode = StatusCodes.Status500InternalServerError;
                errorResponse.Title = "Internal server error.";
                break;
        }

        return errorResponse;
    }

    #endregion
}
