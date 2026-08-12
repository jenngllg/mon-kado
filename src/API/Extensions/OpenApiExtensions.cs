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
        });

        return services;
    }

    public static IEndpointRouteBuilder MapApiOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi(DocumentPath);

        return endpoints;
    }
}
