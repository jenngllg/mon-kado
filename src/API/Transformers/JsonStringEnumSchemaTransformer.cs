using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace JennGllg.Fr.MonKado.Back.Api.Transformers;

/// <summary>
/// Aligns OpenAPI enum schemas with the API's camel-case string JSON contract.
/// </summary>
public class JsonStringEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>
    /// Replaces numeric enum schemas with their supported camel-case string values.
    /// </summary>
    /// <param name="schema">The schema to update.</param>
    /// <param name="context">The schema transformer context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ??
            context.JsonTypeInfo.Type;

        if (!type.IsEnum)
            return Task.CompletedTask;

        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Enum = Enum.GetNames(type)
            .Select(name => JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name)))
            .Cast<JsonNode>()
            .ToArray();

        return Task.CompletedTask;
    }
}
