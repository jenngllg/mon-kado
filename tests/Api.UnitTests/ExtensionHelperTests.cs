using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Http;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class ExtensionHelperTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("name", "name")]
    [InlineData("Parent.Child", "parent.child")]
    public void ToCamelCasePath_WhenValueIsProvided_ReturnsExpectedPath(
        string value,
        string expected)
    {
        // Arrange
        // Act
        var result = ErrorResponseExtensions.ToCamelCasePath(value);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void RequiresAntiforgeryToken_WhenAttributeIsMissing_ReturnsFalse()
    {
        // Arrange
        object[] metadata = [];

        // Act
        var result = OpenApiExtensions.RequiresAntiforgeryToken(metadata);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RequiresAntiforgeryToken_WhenAttributeExists_ReturnsTrue()
    {
        // Arrange
        object[] metadata =
        [
            new Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute()
        ];

        // Act
        var result = OpenApiExtensions.RequiresAntiforgeryToken(metadata);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AddBearerSecurityScheme_WhenComponentsAreMissing_AddsJwtBearerScheme()
    {
        // Arrange
        var document = new Microsoft.OpenApi.OpenApiDocument();

        // Act
        OpenApiExtensions.AddBearerSecurityScheme(document);

        // Assert
        var scheme = Assert.IsType<Microsoft.OpenApi.OpenApiSecurityScheme>(
            document.Components!.SecuritySchemes![OpenApiExtensions.BearerSecuritySchemeName]);
        Assert.Equal(
            Microsoft.OpenApi.SecuritySchemeType.Http,
            scheme.Type);
        Assert.Equal(
            "bearer",
            scheme.Scheme);
        Assert.Equal(
            "JWT",
            scheme.BearerFormat);
    }

    [Fact]
    public void AddBearerSecurityScheme_WhenSecuritySchemesExist_AddsJwtBearerScheme()
    {
        // Arrange
        var document = new Microsoft.OpenApi.OpenApiDocument
        {
            Components = new Microsoft.OpenApi.OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>()
            }
        };

        // Act
        OpenApiExtensions.AddBearerSecurityScheme(document);

        // Assert
        Assert.Contains(
            OpenApiExtensions.BearerSecuritySchemeName,
            document.Components.SecuritySchemes.Keys);
    }

    [Fact]
    public void AddAccessTokenResponseHeaders_WhenSuccessResponseExists_DocumentsCookieAndNoStore()
    {
        // Arrange
        var operation = new Microsoft.OpenApi.OpenApiOperation
        {
            Responses = new Microsoft.OpenApi.OpenApiResponses
            {
                ["200"] = new Microsoft.OpenApi.OpenApiResponse()
            }
        };

        // Act
        OpenApiExtensions.AddAccessTokenResponseHeaders(operation);

        // Assert
        var headers = operation.Responses["200"].Headers!;
        Assert.Contains(
            "__Host-MonKado.Refresh",
            headers["Set-Cookie"].Description,
            StringComparison.Ordinal);
        Assert.Contains(
            "no-store",
            headers["Cache-Control"].Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AddAccessTokenResponseHeaders_WhenSuccessResponseIsMissing_DoesNotCreateResponse()
    {
        // Arrange
        var operation = new Microsoft.OpenApi.OpenApiOperation();

        // Act
        OpenApiExtensions.AddAccessTokenResponseHeaders(operation);

        // Assert
        Assert.True(operation.Responses is null || operation.Responses.Count == 0);
    }

    [Fact]
    public void AddAccessTokenResponseHeaders_WhenResponsesAreMissing_DoesNotCreateResponse()
    {
        // Arrange
        var operation = new Microsoft.OpenApi.OpenApiOperation
        {
            Responses = null!
        };

        // Act
        OpenApiExtensions.AddAccessTokenResponseHeaders(operation);

        // Assert
        Assert.Null(operation.Responses);
    }

    [Fact]
    public void AddAccessTokenResponseHeaders_WhenHeadersExist_PreservesExistingHeader()
    {
        // Arrange
        var response = new Microsoft.OpenApi.OpenApiResponse
        {
            Headers = new Dictionary<string, Microsoft.OpenApi.IOpenApiHeader>
            {
                ["Existing"] = new Microsoft.OpenApi.OpenApiHeader()
            }
        };
        var operation = new Microsoft.OpenApi.OpenApiOperation
        {
            Responses = new Microsoft.OpenApi.OpenApiResponses
            {
                ["200"] = response
            }
        };

        // Act
        OpenApiExtensions.AddAccessTokenResponseHeaders(operation);

        // Assert
        Assert.Contains(
            "Existing",
            response.Headers.Keys);
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    public void CreateLimiter_WhenRemoteAddressIsProvided_UsesExpectedPartition(
        string? remoteAddress,
        string expected)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress is null
            ? null
            : IPAddress.Parse(remoteAddress);

        // Act
        var result = AuthenticationRateLimitingExtensions.CreateLimiter(
            context,
            5);

        // Assert
        Assert.Equal(
            expected,
            result.PartitionKey);
    }

    [Fact]
    public void GetDependencyName_WhenInnerExceptionExists_ReturnsInnerExceptionName()
    {
        // Arrange
        var exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());

        // Act
        var result = GlobalExceptionHandler.GetDependencyName(exception);

        // Assert
        Assert.Equal(
            nameof(TimeoutException),
            result);
    }

    [Fact]
    public void GetDependencyName_WhenInnerExceptionIsMissing_ReturnsDependencyExceptionName()
    {
        // Arrange
        var exception = new DependencyUnavailableException(
            "PostgreSQL",
            null!);

        // Act
        var result = GlobalExceptionHandler.GetDependencyName(exception);

        // Assert
        Assert.Equal(
            nameof(DependencyUnavailableException),
            result);
    }

    [Theory]
    [InlineData("", "", "body", "The request body is invalid.")]
    [InlineData("Parent.Child", "Invalid", "parent.child", "Invalid")]
    public void CreateValidationError_WhenValuesAreProvided_ReturnsNormalizedError(
        string propertyName,
        string errorMessage,
        string expectedPropertyName,
        string expectedErrorMessage)
    {
        // Arrange
        // Act
        var result = ErrorResponseExtensions.CreateValidationError(
            propertyName,
            errorMessage);

        // Assert
        Assert.Equal(
            expectedPropertyName,
            result.PropertyName);
        Assert.Equal(
            expectedErrorMessage,
            result.ErrorMessage);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ShouldAddStrictTransportSecurity_WhenRequestIsProvided_ReturnsExpectedResult(
        bool isProduction,
        bool isHttps,
        bool expected)
    {
        // Arrange
        // Act
        var result = WebSecurityExtensions.ShouldAddStrictTransportSecurity(
            isProduction,
            isHttps);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void CreateCsrfTokenResponse_WhenTokenExists_ReturnsResponse()
    {
        // Arrange
        // Act
        var result = WebSecurityExtensions.CreateCsrfTokenResponse("token");

        // Assert
        Assert.Equal(
            "token",
            result.Token);
    }

    [Fact]
    public void CreateCsrfTokenResponse_WhenTokenIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        static Contracts.Responses.CsrfTokenResponse action() => WebSecurityExtensions.CreateCsrfTokenResponse(null);

        // Assert
        Assert.Throws<InvalidOperationException>((Func<Contracts.Responses.CsrfTokenResponse>)action);
    }

    [Fact]
    public void AddAntiforgeryParameter_WhenParametersAreMissing_CreatesCollection()
    {
        // Arrange
        var operation = new Microsoft.OpenApi.OpenApiOperation();

        // Act
        OpenApiExtensions.AddAntiforgeryParameter(operation);

        // Assert
        Assert.Single(operation.Parameters!);
    }

    [Fact]
    public void AddAntiforgeryParameter_WhenParametersExist_AppendsParameter()
    {
        // Arrange
        var operation = new Microsoft.OpenApi.OpenApiOperation
        {
            Parameters =
            [
                new Microsoft.OpenApi.OpenApiParameter()
            ]
        };

        // Act
        OpenApiExtensions.AddAntiforgeryParameter(operation);

        // Assert
        Assert.Equal(
            2,
            operation.Parameters.Count);
    }
}
