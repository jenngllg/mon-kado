using System.Diagnostics;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                Dictionary<string, string[]> errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(
                        entry => string.IsNullOrWhiteSpace(entry.Key) ? "body" : entry.Key,
                        entry => entry.Value!.Errors
                            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? "The request body is invalid."
                                : error.ErrorMessage)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal);

                ValidationProblemDetails problem = new(errors)
                {
                    Type = "https://api.mon-kado.fr/problems/validation-error",
                    Title = "Validation failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "One or more fields are invalid.",
                    Instance = context.HttpContext.Request.Path
                };
                problem.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                problem.Extensions["code"] = "VALIDATION_ERROR";
                context.HttpContext.Response.Headers.CacheControl = "no-store";

                BadRequestObjectResult result = new(problem);
                result.ContentTypes.Add("application/problem+json");
                return result;
            });

        return services;
    }
}
