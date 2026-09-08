using Microsoft.AspNetCore.OpenApi;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace JennGllg.Fr.MonKado.Back.Api.Transformers;

/// <summary>Documents private ZIP responses and asynchronous export status locations.</summary>
public class PersonalDataExportOperationTransformer : IOpenApiOperationTransformer
{
    private const string ExportPath = "api/v1/members/current/data-exports";
    /// <inheritdoc/>
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath;

        if (path is null || !(path.StartsWith(
            ExportPath,
            StringComparison.Ordinal) || path.StartsWith(
            "api/v1/admin/members/{memberId}/data-exports",
            StringComparison.Ordinal)) || operation.Responses is null)
            return Task.CompletedTask;

        if (context.Description.HttpMethod == HttpMethods.Post)
        {
            AddHeader(
                operation.Responses["200"],
                HeaderNames.Location,
                "URI reference of the reused export status resource.");
            AddHeader(
                operation.Responses["202"],
                HeaderNames.Location,
                "URI reference of the pending export status resource.");
        }

        if (path.EndsWith(
            "/archive",
            StringComparison.Ordinal))
        {
            var response = (OpenApiResponse)operation.Responses["200"];
            response.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/zip"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "binary"
                    }
                }
            };
            AddHeader(
                response,
                HeaderNames.ContentDisposition,
                "Attachment with a generated identifier-only ZIP filename.");
            AddHeader(
                response,
                HeaderNames.XContentTypeOptions,
                "Always nosniff.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Adds an explicit non-sensitive response header without replacing existing headers.</summary>
    /// <param name="response">The generated response.</param>
    /// <param name="name">The HTTP header name.</param>
    /// <param name="description">The header contract.</param>
    private static void AddHeader(
        IOpenApiResponse response,
        string name,
        string description)
    {
        var mutable = (OpenApiResponse)response;
        var headers = new Dictionary<string, IOpenApiHeader>(
            mutable.Headers ?? new Dictionary<string, IOpenApiHeader>(),
            StringComparer.OrdinalIgnoreCase)
        {
            [name] = new OpenApiHeader
            {
                Description = description,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                }
            }
        };
        mutable.Headers = headers;
    }
}
