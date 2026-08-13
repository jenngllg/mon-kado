using JennGllg.Fr.MonKado.Back.Api.Security;
using Microsoft.OpenApi;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string DocumentPath = "/openapi/{documentName}.json";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Mon Kado API";
                document.Info.Version = DocumentName;
                document.Info.Description = "API for creating, sharing, and managing gift wishlists.";

                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (!RequiresAntiforgeryToken(context.Description.HttpMethod))
                {
                    return Task.CompletedTask;
                }

                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = WebSecurityOptions.AntiforgeryHeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Request token obtained from GET /security/csrf-token.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder MapApiOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi(DocumentPath);

        return endpoints;
    }

    private static bool RequiresAntiforgeryToken(string? httpMethod)
    {
        return httpMethod is not null &&
            (HttpMethods.IsPost(httpMethod) || HttpMethods.IsPut(httpMethod) ||
             HttpMethods.IsPatch(httpMethod) || HttpMethods.IsDelete(httpMethod));
    }
}
