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

    [Theory]
    [InlineData(null, false)]
    [InlineData("GET", false)]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    public void RequiresAntiforgeryToken_WhenMethodIsProvided_ReturnsExpectedResult(
        string? method,
        bool expected)
    {
        // Arrange
        // Act
        var result = OpenApiExtensions.RequiresAntiforgeryToken(method);

        // Assert
        Assert.Equal(
            expected,
            result);
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
