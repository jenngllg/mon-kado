using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Transformers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class CommonErrorResponsesOperationTransformerTests
{
    [Fact]
    public void AddResponse_WhenResponsesAreMissing_CreatesResponses()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Responses = null!
        };

        // Act
        CommonErrorResponsesOperationTransformer.AddResponse(
            operation,
            500,
            "Internal server error",
            new OpenApiSchema());

        // Assert
        Assert.NotNull(operation.Responses);
        Assert.Single(operation.Responses);
    }

    [Fact]
    public void AddAuthorizationResponses_WhenAuthorizationIsRequired_AddsUnauthorizedAndForbidden()
    {
        // Arrange
        var operation = new OpenApiOperation();
        var schema = new OpenApiSchema();
        var metadata = new object[]
        {
            new AuthorizeAttribute()
        };

        // Act
        CommonErrorResponsesOperationTransformer.AddAuthorizationResponses(
            operation,
            schema,
            metadata);

        // Assert
        Assert.NotNull(operation.Responses);
        Assert.Equal(
            2,
            operation.Responses.Count);
        Assert.Equal(
            "Authentication is required",
            operation.Responses["401"].Description);
        Assert.Equal(
            "The authenticated user is not authorized",
            operation.Responses["403"].Description);
    }

    [Fact]
    public void AddBearerSecurityRequirement_WhenAuthorizationIsRequired_AddsBearerReference()
    {
        // Arrange
        var operation = new OpenApiOperation();
        var document = new OpenApiDocument();
        OpenApiExtensions.AddBearerSecurityScheme(document);

        // Act
        CommonErrorResponsesOperationTransformer.AddBearerSecurityRequirement(
            operation,
            document);

        // Assert
        var requirement = Assert.Single(operation.Security!);
        var scheme = Assert.Single(requirement.Keys);
        Assert.Equal(
            OpenApiExtensions.BearerSecuritySchemeName,
            scheme.Reference.Id);
        Assert.Empty(requirement[scheme]);
    }

    [Fact]
    public void AddBearerSecurityRequirement_WhenSecurityExists_AppendsBearerReference()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Security =
            [
                new OpenApiSecurityRequirement()
            ]
        };

        // Act
        CommonErrorResponsesOperationTransformer.AddBearerSecurityRequirement(
            operation,
            null);

        // Assert
        Assert.Equal(
            2,
            operation.Security.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddAuthorizationResponses_WhenAuthorizationIsNotRequired_DoesNotAddResponses(
        bool allowsAnonymous)
    {
        // Arrange
        var operation = new OpenApiOperation();
        var schema = new OpenApiSchema();
        var metadata = allowsAnonymous
            ? new object[]
            {
                new AuthorizeAttribute(),
                new AllowAnonymousAttribute()
            }
            : [];

        // Act
        CommonErrorResponsesOperationTransformer.AddAuthorizationResponses(
            operation,
            schema,
            metadata);

        // Assert
        Assert.True(operation.Responses is null || operation.Responses.Count == 0);
    }

    [Fact]
    public void AddResponse_WhenResponsesAlreadyExist_PreservesExistingResponse()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["204"] = new OpenApiResponse { Description = "No content" }
            }
        };
        var schema = new OpenApiSchema();

        // Act
        CommonErrorResponsesOperationTransformer.AddResponse(
            operation,
            500,
            "Internal server error",
            schema);

        // Assert
        Assert.Equal(
            2,
            operation.Responses.Count);
        Assert.Same(
            schema,
            operation.Responses["500"].Content!["application/json"].Schema);
    }

    [Fact]
    public void AddResponse_WhenStatusAlreadyExists_ReplacesExistingStatusResponse()
    {
        // Arrange
        var existing = new OpenApiResponse { Description = "Existing" };
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["500"] = existing
            }
        };

        // Act
        CommonErrorResponsesOperationTransformer.AddResponse(
            operation,
            500,
            "Replacement",
            new OpenApiSchema());

        // Assert
        Assert.NotSame(
            existing,
            operation.Responses["500"]);
        Assert.Equal(
            "Replacement",
            operation.Responses["500"].Description);
    }
}
