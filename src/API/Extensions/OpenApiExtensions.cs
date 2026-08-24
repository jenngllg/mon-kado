using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Api.Transformers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

using System.Text.Json.Nodes;

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

                AddRememberMeDefault(operation);

                var refreshTokenCookie = metadata
                    .OfType<RefreshTokenCookieAttribute>()
                    .SingleOrDefault();

                if (refreshTokenCookie is not null)
                    AddRefreshTokenCookieParameter(
                        operation,
                        refreshTokenCookie.IsRequired);

                var googleExternalCookie = metadata
                    .OfType<GoogleExternalCookieAttribute>()
                    .SingleOrDefault();

                if (googleExternalCookie is not null)
                    AddGoogleExternalCookieParameter(
                        operation,
                        googleExternalCookie.IsRequired);

                if (metadata.OfType<GoogleFlowBindingAttribute>().Any())
                    AddGoogleFlowBindingParameter(operation);

                if (ReturnsAccessToken(metadata))
                    AddAccessTokenResponseHeaders(operation);

                if (DeletesRefreshTokenCookie(metadata))
                    AddDeletedRefreshTokenResponseHeaders(operation);

                if (ReturnsRedirect(metadata))
                    AddRedirectResponseHeaders(operation);

                if (ReturnsGoogleExternalCookie(metadata))
                    AddGoogleExternalCookieResponseHeaders(operation);

                foreach (var noStoreResponse in metadata.OfType<NoStoreResponseAttribute>())
                    AddNoStoreResponseHeader(
                        operation,
                        noStoreResponse.StatusCode);

                var entityTag = metadata
                    .OfType<EntityTagAttribute>()
                    .SingleOrDefault();

                if (entityTag is not null)
                    AddEntityTagContract(
                        operation,
                        entityTag.IsRequired,
                        entityTag.ReturnsEntityTag);

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

    private static bool RequiresAntiforgeryToken(IEnumerable<object> metadata)
    {
        return metadata.OfType<ValidateAntiForgeryTokenAttribute>().Any();
    }

    private static void AddBearerSecurityScheme(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Components);
        document.Components.SecuritySchemes = AddOrReplace(
            document.Components.SecuritySchemes,
            BearerSecuritySchemeName,
            new OpenApiSecurityScheme
            {
                BearerFormat = "JWT",
                Description = "JWT access token returned by the login or refresh endpoint.",
                Scheme = "bearer",
                Type = SecuritySchemeType.Http
            });
    }

    private static bool ReturnsAccessToken(IEnumerable<object> metadata)
    {
        return metadata.OfType<ProducesResponseTypeAttribute>().Any(attribute =>
            attribute.StatusCode == StatusCodes.Status200OK &&
            attribute.Type == typeof(AccessTokenResponse));
    }

    private static void AddAccessTokenResponseHeaders(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Responses);
        var response = operation.Responses[
            StatusCodes.Status200OK.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var mutableResponse = (OpenApiResponse)response;
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.SetCookie,
            new OpenApiHeader
            {
                Description =
                    "Rotating refresh token cookie. It is HttpOnly, SameSite=Strict, host-only, and uses Path=/. " +
                    "Production uses the Secure __Host-MonKado.Refresh name; local development uses MonKado.Refresh. " +
                    "It is a browser-session cookie unless rememberMe requests the fixed 30-day expiration.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.CacheControl,
            new OpenApiHeader
            {
                Description = "Always no-store for token responses.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddAntiforgeryParameter(OpenApiOperation operation)
    {
        operation.Parameters = AddItem(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = WebSecurityOptions.AntiforgeryHeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Description = "Request token obtained from GET /security/csrf-token.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddRememberMeDefault(OpenApiOperation operation)
    {
        var parameter = operation.Parameters?
            .OfType<OpenApiParameter>()
            .SingleOrDefault(parameter =>
                parameter.In == ParameterLocation.Query &&
                string.Equals(
                    parameter.Name,
                    "rememberMe",
                    StringComparison.Ordinal));

        if (parameter?.Schema is not OpenApiSchema schema)
            return;

        parameter.Required = false;
        schema.Default = JsonValue.Create(false);
    }

    private static bool DeletesRefreshTokenCookie(IEnumerable<object> metadata)
    {

        return metadata.OfType<DeletesRefreshTokenCookieAttribute>().Any();
    }

    private static bool ReturnsRedirect(IEnumerable<object> metadata)
    {

        return metadata
            .OfType<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == StatusCodes.Status302Found);
    }

    private static bool ReturnsGoogleExternalCookie(IEnumerable<object> metadata)
    {

        return metadata.OfType<ReturnsGoogleExternalCookieAttribute>().Any();
    }

    private static void AddRedirectResponseHeaders(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Responses);
        var response = operation.Responses[
            StatusCodes.Status302Found.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var mutableResponse = (OpenApiResponse)response;
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.Location,
            new OpenApiHeader
            {
                Description =
                    "Redirect destination for the provider challenge or callback completion/failure route. " +
                    "A successful callback completion route includes only an opaque flow binding. " +
                    "It never contains an access token, refresh token or identity claim.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddGoogleExternalCookieResponseHeaders(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Responses);
        var response = operation.Responses[
            StatusCodes.Status302Found.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var mutableResponse = (OpenApiResponse)response;
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.SetCookie,
            new OpenApiHeader
            {
                Description =
                    "Issues a five-minute HttpOnly, Secure, SameSite=Lax and host-only Google external cookie. " +
                    "It contains no Google token, MonKado token or unprotected identity claim.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddRefreshTokenCookieParameter(
        OpenApiOperation operation,
        bool isRequired)
    {
        operation.Parameters = AddItem(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = RefreshTokenCookieService.ProductionCookieName,
                In = ParameterLocation.Cookie,
                Required = isRequired,
                Description =
                    "HttpOnly rotating refresh token cookie. Production uses __Host-MonKado.Refresh; " +
                    "local development uses MonKado.Refresh.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddGoogleExternalCookieParameter(
        OpenApiOperation operation,
        bool isRequired)
    {
        operation.Parameters = AddItem(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = GoogleAuthenticationConstants.ProductionExternalCookieName,
                In = ParameterLocation.Cookie,
                Required = isRequired,
                Description = string.Concat(
                    "Short-lived Data Protection cookie containing only validated Google identity claims and protected flow state. ",
                    "It is HttpOnly, Secure, SameSite=Lax, host-only and expires after five minutes. Local development uses ",
                    GoogleAuthenticationConstants.LocalExternalCookieName,
                    ". It never contains Google or MonKado tokens."),
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddGoogleFlowBindingParameter(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Parameters);
        var parameter = operation.Parameters
            .OfType<OpenApiParameter>()
            .Single(parameter =>
                parameter.In == ParameterLocation.Query &&
                string.Equals(
                    parameter.Name,
                    GoogleAuthenticationConstants.FlowBindingParameter,
                    StringComparison.Ordinal));

        parameter.Required = true;
        parameter.Description =
            "Opaque five-minute browser-flow binding returned in the frontend redirect fragment. " +
            "It is required to prevent concurrent Google flows from being crossed and is not an access, refresh or Google token.";
    }

    private static void AddDeletedRefreshTokenResponseHeaders(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Responses);
        var response = operation.Responses[
            StatusCodes.Status204NoContent.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var mutableResponse = (OpenApiResponse)response;
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.SetCookie,
            new OpenApiHeader
            {
                Description =
                    "Deletes the HttpOnly refresh token cookie for the current browser session.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.CacheControl,
            new OpenApiHeader
            {
                Description = "Always no-store for successful responses that delete the refresh cookie.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddNoStoreResponseHeader(
        OpenApiOperation operation,
        int statusCode)
    {
        ArgumentNullException.ThrowIfNull(operation.Responses);
        var response = operation.Responses[
            statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var mutableResponse = (OpenApiResponse)response;
        mutableResponse.Headers = AddOrReplace(
            mutableResponse.Headers,
            HeaderNames.CacheControl,
            new OpenApiHeader
            {
                Description = "Always no-store for this response.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static void AddEntityTagContract(
        OpenApiOperation operation,
        bool isRequired,
        bool returnsEntityTag)
    {
        ArgumentNullException.ThrowIfNull(operation.Responses);

        if (returnsEntityTag)
        {
            var response = operation.Responses[
                StatusCodes.Status200OK.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            var mutableResponse = (OpenApiResponse)response;
            mutableResponse.Headers = AddOrReplace(
                mutableResponse.Headers,
                HeaderNames.ETag,
                new OpenApiHeader
                {
                    Description = "Strong entity tag representing the current member profile version.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });
        }

        if (!isRequired)
            return;

        operation.Parameters = AddItem(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = HeaderNames.IfMatch,
                In = ParameterLocation.Header,
                Required = true,
                Description = "Strong entity tag returned by the current session or the previous profile update.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
    }

    private static IDictionary<string, TValue> AddOrReplace<TValue>(
        IDictionary<string, TValue>? values,
        string key,
        TValue value)
    {
        values ??= new Dictionary<string, TValue>();
        values[key] = value;

        return values;
    }

    private static IList<TValue> AddItem<TValue>(
        IList<TValue>? values,
        TValue value)
    {
        values ??= [];
        values.Add(value);

        return values;
    }
}
