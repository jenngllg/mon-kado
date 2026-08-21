namespace JennGllg.Fr.MonKado.Back.Api.Errors;
/// <summary>
/// Represents api status code response factory.
/// </summary>

public static class ApiStatusCodeResponseFactory
{
    /// <summary>
    /// Executes the create operation.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The operation result.</returns>
    public static ErrorResponse Create(int statusCode)
    {

        return statusCode switch
        {
            StatusCodes.Status400BadRequest => new ErrorResponse(
                statusCode,
                "Bad request",
                "The request is invalid.",
                ErrorCodes.RequestBadRequest,
                null),
            StatusCodes.Status401Unauthorized => new ErrorResponse(
                statusCode,
                "Unauthorized",
                "Authentication is required.",
                ErrorCodes.SecurityUnauthorized,
                null),
            StatusCodes.Status403Forbidden => new ErrorResponse(
                statusCode,
                "Forbidden",
                "The authenticated user is not allowed to perform this operation.",
                ErrorCodes.SecurityForbidden,
                null),
            StatusCodes.Status404NotFound => new ErrorResponse(
                statusCode,
                "Not found",
                "The requested resource was not found.",
                ErrorCodes.RequestNotFound,
                null),
            StatusCodes.Status413PayloadTooLarge => new ErrorResponse(
                statusCode,
                "Payload too large",
                "The request body is too large.",
                ErrorCodes.RequestPayloadTooLarge,
                null),
            StatusCodes.Status415UnsupportedMediaType => new ErrorResponse(
                statusCode,
                "Unsupported media type",
                "The request content type is not supported.",
                ErrorCodes.RequestUnsupportedMediaType,
                null),
            StatusCodes.Status503ServiceUnavailable => new ErrorResponse(
                statusCode,
                "Service temporarily unavailable",
                "The service is temporarily unavailable. Retry later.",
                ErrorCodes.TechnicalServiceUnavailable,
                null),
            _ => new ErrorResponse(
                statusCode,
                "Request failed",
                "The request could not be completed.",
                null,
                null)
        };
    }
}
