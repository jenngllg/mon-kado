using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Transformers;

using Microsoft.OpenApi;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents open api extensions.
/// </summary>

public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string DocumentPath = "/openapi/{documentName}.json";
    /// <summary>
    /// Executes the add api open api operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(
            DocumentName,
            options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer((
                document,
                _,
                _) =>
            {
                document.Info.Title = "Mon Kado API";
                document.Info.Version = DocumentName;
                document.Info.Description = "API for creating, sharing, and managing gift wishlists.";

                return Task.CompletedTask;
            });
            options.AddOperationTransformer((
                operation,
                context,
                _) =>
            {

                if (!RequiresAntiforgeryToken(context.Description.HttpMethod))
                    return Task.CompletedTask;

                AddAntiforgeryParameter(operation);

                return Task.CompletedTask;
            });
            options.AddOperationTransformer<CommonErrorResponsesOperationTransformer>();
        });

        return services;
    }
    /// <summary>
    /// Executes the map api open api operation.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <returns>The operation result.</returns>

    public static IEndpointRouteBuilder MapApiOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi(DocumentPath);

        return endpoints;
    }

    internal static bool RequiresAntiforgeryToken(string? httpMethod)
    {

        return httpMethod is not null &&
            (HttpMethods.IsPost(httpMethod) || HttpMethods.IsPut(httpMethod) ||
             HttpMethods.IsPatch(httpMethod) || HttpMethods.IsDelete(httpMethod));
    }

    internal static void AddAntiforgeryParameter(OpenApiOperation operation)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = WebSecurityOptions.AntiforgeryHeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "Request token obtained from GET /security/csrf-token.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }
}
