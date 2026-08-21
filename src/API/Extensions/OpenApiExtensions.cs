using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Transformers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents open api extensions.
/// </summary>

public static class OpenApiExtensions
{
    internal const string BearerSecuritySchemeName = "Bearer";
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
                AddBearerSecurityScheme(document);

                return Task.CompletedTask;
            });
            options.AddOperationTransformer((
                operation,
                context,
                _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;

                if (RequiresAntiforgeryToken(metadata))
                    AddAntiforgeryParameter(operation);

                if (ReturnsAccessToken(metadata))
                    AddAccessTokenResponseHeaders(operation);

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

    internal static bool RequiresAntiforgeryToken(IEnumerable<object> metadata)
    {
        return metadata.OfType<ValidateAntiForgeryTokenAttribute>().Any();
    }

    internal static void AddBearerSecurityScheme(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerSecuritySchemeName] =
            new OpenApiSecurityScheme
            {
                BearerFormat = "JWT",
                Description = "JWT access token returned by the login or refresh endpoint.",
                Scheme = "bearer",
                Type = SecuritySchemeType.Http
            };
    }

    internal static bool ReturnsAccessToken(IEnumerable<object> metadata)
    {
        return metadata.OfType<ProducesResponseTypeAttribute>().Any(attribute =>
            attribute.StatusCode == StatusCodes.Status200OK &&
            attribute.Type == typeof(AccessTokenResponse));
    }

    internal static void AddAccessTokenResponseHeaders(OpenApiOperation operation)
    {
        if (operation.Responses is null ||
            !operation.Responses.TryGetValue(
                StatusCodes.Status200OK.ToString(System.Globalization.CultureInfo.InvariantCulture),
                out var response))
            return;

        var mutableResponse = (OpenApiResponse)response;

        mutableResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
        mutableResponse.Headers[HeaderNames.SetCookie] = new OpenApiHeader
        {
            Description =
                "Rotating refresh token cookie. It is HttpOnly, SameSite=Strict, host-only, and uses Path=/. " +
                "Production uses the Secure __Host-MonKado.Refresh name; local development uses MonKado.Refresh. " +
                "It is a browser-session cookie unless rememberMe requests the fixed 30-day expiration.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        };
        mutableResponse.Headers[HeaderNames.CacheControl] = new OpenApiHeader
        {
            Description = "Always no-store for token responses.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        };
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
