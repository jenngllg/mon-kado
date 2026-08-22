using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Handlers;
using JennGllg.Fr.MonKado.Back.Api.Logging;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents error response extensions.
/// </summary>

public static class ErrorResponseExtensions
{
    /// <summary>
    /// Executes the add api error responses operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The operation result.</returns>
    public static IServiceCollection AddApiErrorResponses(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // ASP.NET Core requires a fallback service even though the global handler handles every exception.
        services.AddProblemDetails();
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                var validationErrors = context.ModelState
                    .SelectMany(entry => GetErrors(entry.Value).Select(error => CreateValidationError(
                        entry.Key,
                        error.ErrorMessage)))
                    .DistinctBy(error => new
                    {
                        error.PropertyName,
                        error.ErrorMessage
                    })
                    .ToArray();

                var errorResponse = new ErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "Validation failed",
                    "One or more fields are invalid.",
                    ErrorCodes.RequestValidationError,
                    validationErrors);
                var loggerFactory = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger(typeof(ErrorResponseExtensions));
                ApiLogMessages.ExpectedHttpError(
                    logger,
                    errorResponse.StatusCode,
                    ErrorCodes.RequestValidationError);
                context.HttpContext.Response.Headers.CacheControl = "no-store";

                var result = new BadRequestObjectResult(errorResponse);
                result.ContentTypes.Add("application/json");

                return result;
            });

        return services;
    }

    private static string ToCamelCasePath(string value)
    {
        return string.Join(
            '.',
            value
                .Split('.')
                .Select(JsonNamingPolicy.CamelCase.ConvertName));
    }

    private static ValidationError CreateValidationError(
        string propertyName,
        string errorMessage)
    {

        return new ValidationError(
            string.IsNullOrWhiteSpace(propertyName) ? "body" : ToCamelCasePath(propertyName),
            string.IsNullOrWhiteSpace(errorMessage)
                ? "The request body is invalid."
                : errorMessage);
    }

    private static ModelErrorCollection GetErrors(ModelStateEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Errors;
    }
}
