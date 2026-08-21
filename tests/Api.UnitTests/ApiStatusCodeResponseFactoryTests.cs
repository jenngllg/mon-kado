using JennGllg.Fr.MonKado.Back.Api.Errors;

using Microsoft.AspNetCore.Http;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class ApiStatusCodeResponseFactoryTests
{
    public static TheoryData<int, string, string?> Cases
    {
        get
        {
            return new()
            {
                {
                    StatusCodes.Status400BadRequest,
                    "Bad request",
                    ErrorCodes.RequestBadRequest
                },
                {
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    ErrorCodes.SecurityUnauthorized
                },
                {
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    ErrorCodes.SecurityForbidden
                },
                {
                    StatusCodes.Status404NotFound,
                    "Not found",
                    ErrorCodes.RequestNotFound
                },
                {
                    StatusCodes.Status413PayloadTooLarge,
                    "Payload too large",
                    ErrorCodes.RequestPayloadTooLarge
                },
                {
                    StatusCodes.Status415UnsupportedMediaType,
                    "Unsupported media type",
                    ErrorCodes.RequestUnsupportedMediaType
                },
                {
                    StatusCodes.Status503ServiceUnavailable,
                    "Service temporarily unavailable",
                    ErrorCodes.TechnicalServiceUnavailable
                },
                {
                    StatusCodes.Status418ImATeapot,
                    "Request failed",
                    null
                }
            };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Create_WhenStatusCodeIsProvided_ReturnsExpectedResponse(
        int statusCode,
        string expectedTitle,
        string? expectedErrorCode)
    {
        // Arrange

        // Act
        var response = ApiStatusCodeResponseFactory.Create(statusCode);

        // Assert
        Assert.Equal(
            statusCode,
            response.StatusCode);
        Assert.Equal(
            expectedTitle,
            response.Title);
        Assert.Equal(
            expectedErrorCode,
            response.ErrorCode);
        Assert.Null(response.ValidationErrors);
    }
}
