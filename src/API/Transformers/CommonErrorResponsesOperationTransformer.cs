using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using System.Globalization;

namespace JennGllg.Fr.MonKado.Back.Api.Transformers;

/// <summary>
/// Adds the error responses shared by API operations to the OpenAPI contract.
/// </summary>
public class CommonErrorResponsesOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>
    /// Adds the common error responses to an OpenAPI operation.
    /// </summary>
    /// <param name="operation">The operation to update.</param>
    /// <param name="context">The transformer context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the transformation.</returns>
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schema = await context.GetOrCreateSchemaAsync(
            typeof(ErrorResponse),
            parameterDescription: null,
            cancellationToken);

        AddResponse(
            operation,
            StatusCodes.Status500InternalServerError,
            "Internal server error",
            schema);

        AddAuthorizationResponses(
            operation,
            schema,
            context.Description.ActionDescriptor.EndpointMetadata);

        if (RequiresAuthorization(context.Description.ActionDescriptor.EndpointMetadata))
            AddBearerSecurityRequirement(
                operation,
                context.Document);
    }

    internal static void AddAuthorizationResponses(
        OpenApiOperation operation,
        IOpenApiSchema schema,
        IEnumerable<object> metadata)
    {
        if (!RequiresAuthorization(metadata))
            return;

        AddResponse(
            operation,
            StatusCodes.Status401Unauthorized,
            "Authentication is required",
            schema);
        AddResponse(
            operation,
            StatusCodes.Status403Forbidden,
            "The authenticated user is not authorized",
            schema);
    }

    internal static bool RequiresAuthorization(IEnumerable<object> metadata)
    {
        return metadata.OfType<IAuthorizeData>().Any() &&
            !metadata.OfType<IAllowAnonymous>().Any();
    }

    internal static void AddBearerSecurityRequirement(
        OpenApiOperation operation,
        OpenApiDocument? document)
    {
        var scheme = new OpenApiSecuritySchemeReference(
            OpenApiExtensions.BearerSecuritySchemeName,
            document,
            null);
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [scheme] = []
        });
    }

    internal static void AddResponse(
        OpenApiOperation operation,
        int statusCode,
        string description,
        IOpenApiSchema schema)
    {
        var response = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = schema
                }
            }
        };
        operation.Responses ??= [];
        operation.Responses[statusCode.ToString(CultureInfo.InvariantCulture)] = response;
    }
}
