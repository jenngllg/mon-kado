using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Handlers;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_WhenAccessTokenIsInvalid_PreservesTheIndependentRefreshCookie()
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await _handler.TryHandleAsync(
            context,
            new InvalidAccessTokenException(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TryHandleAsync_WhenEndpointMetadataIsAvailableOrMissing_AppliesItsRefreshCookiePolicy(
        bool hasEndpoint,
        bool preserve)
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        if (hasEndpoint)
            context.SetEndpoint(new Endpoint(
                    null,
                    preserve ? new EndpointMetadataCollection(new PreserveRefreshCookieAttribute()) : EndpointMetadataCollection.Empty,
                    "export"));

        if (!preserve)
            _refreshTokenCookieServiceMock.Setup(service => service.Delete(context));

        // Act
        await _handler.TryHandleAsync(
            context,
            new InvalidAuthenticationSessionException(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);

        if (!preserve)
            _refreshTokenCookieServiceMock.Verify(
                service => service.Delete(context),
                Times.Once);
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryHandleAsync_WhenImageStorageFails_DoesNotLogExceptionPaths()
    {
        // Arrange
        var logger = new RecordingExceptionLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(
            logger,
            _refreshTokenCookieServiceMock.Object,
            _googleExternalAuthenticationServiceMock.Object,
            _guestSessionCookieServiceMock.Object);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var exception = new GiftImageStorageUnavailableException(new IOException("private-storage-path"));

        // Act
        await handler.TryHandleAsync(
            context,
            exception,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            context.Response.StatusCode);
        Assert.Single(logger.Entries);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Contains(
                "private-storage-path",
                StringComparison.Ordinal));
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
    }

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Mock<IGoogleExternalAuthenticationService> _googleExternalAuthenticationServiceMock;
    private readonly Mock<IGuestSessionCookieService> _guestSessionCookieServiceMock;
    private readonly Mock<IRefreshTokenCookieService> _refreshTokenCookieServiceMock;
    private readonly GlobalExceptionHandler _handler;
    public GlobalExceptionHandlerTests()
    {
        _googleExternalAuthenticationServiceMock = new Mock<IGoogleExternalAuthenticationService>(MockBehavior.Strict);
        _guestSessionCookieServiceMock = new Mock<IGuestSessionCookieService>(MockBehavior.Strict);
        _refreshTokenCookieServiceMock = new Mock<IRefreshTokenCookieService>(MockBehavior.Strict);
        _handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            _refreshTokenCookieServiceMock.Object,
            _googleExternalAuthenticationServiceMock.Object,
            _guestSessionCookieServiceMock.Object);
    }

    public static TheoryData<Exception, int, string, bool> GoogleFailures => new()
    {
        {
            new GoogleFlowBindingMismatchException(),
            StatusCodes.Status401Unauthorized,
            ErrorCodes.GoogleAuthenticationFailed,
            false
        },
        {
            new GoogleAccountLinkRequiredException(),
            StatusCodes.Status409Conflict,
            ErrorCodes.GoogleAccountLinkRequired,
            false
        },
        {
            new GoogleAdditionalVerificationRequiredException(),
            StatusCodes.Status409Conflict,
            ErrorCodes.GoogleAdditionalVerificationRequired,
            true
        },
        {
            new GoogleAuthenticationFailedException(),
            StatusCodes.Status401Unauthorized,
            ErrorCodes.GoogleAuthenticationFailed,
            true
        },
        {
            new GoogleAccountLinkFailedException(),
            StatusCodes.Status401Unauthorized,
            ErrorCodes.GoogleAccountLinkFailed,
            false
        },
        {
            new GoogleAccountLinkConflictException(),
            StatusCodes.Status409Conflict,
            ErrorCodes.GoogleAccountLinkConflict,
            true
        }
    };

    [Theory]
    [MemberData(nameof(GoogleFailures))]
    public async Task TryHandleAsync_WhenGoogleFailureOccurs_ReturnsStructuredError(
        Exception exception,
        int expectedStatusCode,
        string expectedErrorCode,
        bool deletesExternalCookie)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (deletesExternalCookie)
            _googleExternalAuthenticationServiceMock
                .Setup(service => service.DeleteAsync(
                    context,
                    TestContext.Current.CancellationToken))
                .Returns(Task.CompletedTask);

        // Act
        var wasHandled = await _handler.TryHandleAsync(
            context,
            exception,
            TestContext.Current.CancellationToken);
        context.Response.Body.Position = 0;
        var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            context.Response.Body,
            _jsonOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(wasHandled);
        Assert.Equal(
            expectedStatusCode,
            context.Response.StatusCode);
        Assert.Equal(
            expectedErrorCode,
            error?.ErrorCode);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);

        if (deletesExternalCookie)
            _googleExternalAuthenticationServiceMock.Verify(
                service => service.DeleteAsync(
                    context,
                    TestContext.Current.CancellationToken),
                Times.Once);
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryHandleAsync_WhenRequestBodyIsTooLarge_ReturnsStructuredPayloadTooLarge()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var exception = new BadHttpRequestException(
            "The request body is too large.",
            StatusCodes.Status413PayloadTooLarge);

        // Act
        var wasHandled = await _handler.TryHandleAsync(
            context,
            exception,
            TestContext.Current.CancellationToken);
        context.Response.Body.Position = 0;
        var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            context.Response.Body,
            _jsonOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(wasHandled);
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            context.Response.StatusCode);
        Assert.Equal(
            ErrorCodes.RequestPayloadTooLarge,
            error?.ErrorCode);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(WishlistParticipantNotFoundException), StatusCodes.Status404NotFound, ErrorCodes.WishlistParticipantNotFound)]
    [InlineData(nameof(WishlistOwnerCannotJoinException), StatusCodes.Status409Conflict, ErrorCodes.WishlistOwnerCannotJoin)]
    [InlineData(nameof(WishlistParticipantLimitReachedException), StatusCodes.Status409Conflict, ErrorCodes.WishlistParticipantLimitReached)]
    public async Task TryHandleAsync_WhenParticipantFailureOccurs_ReturnsStructuredError(
        string exceptionName,
        int expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var exception = exceptionName switch
        {
            nameof(WishlistParticipantNotFoundException) => (Exception)new WishlistParticipantNotFoundException(),
            nameof(WishlistOwnerCannotJoinException) => new WishlistOwnerCannotJoinException(),
            nameof(WishlistParticipantLimitReachedException) => new WishlistParticipantLimitReachedException(),
            _ => throw new InvalidOperationException("Unknown participant failure.")
        };

        // Act
        await _handler.TryHandleAsync(
            context,
            exception,
            TestContext.Current.CancellationToken);
        context.Response.Body.Position = 0;
        var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            context.Response.Body,
            _jsonOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            context.Response.StatusCode);
        Assert.Equal(
            expectedErrorCode,
            error?.ErrorCode);
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryHandleAsync_WhenGuestSessionIsInvalid_DeletesGuestCookie()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        _guestSessionCookieServiceMock.Setup(service => service.Delete(context));

        // Act
        await _handler.TryHandleAsync(
            context,
            new GuestSessionInvalidException(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);
        _guestSessionCookieServiceMock.Verify(
            service => service.Delete(context),
            Times.Once);
        _googleExternalAuthenticationServiceMock.VerifyNoOtherCalls();
        _refreshTokenCookieServiceMock.VerifyNoOtherCalls();
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
    }
}
