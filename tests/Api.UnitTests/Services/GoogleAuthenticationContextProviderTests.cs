using AutoFixture;

using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.Http;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GoogleAuthenticationContextProviderTests
{
    private const string Flow = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly HttpContextAccessor _accessor;
    private readonly Mock<IGoogleExternalAuthenticationService> _externalAuthenticationServiceMock;
    private readonly GoogleAuthenticationContextProvider _provider;
    public GoogleAuthenticationContextProviderTests()
    {
        _accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        _externalAuthenticationServiceMock = new Mock<IGoogleExternalAuthenticationService>(MockBehavior.Strict);
        _provider = new GoogleAuthenticationContextProvider(
            _accessor,
            _externalAuthenticationServiceMock.Object);
    }

    [Fact]
    public async Task GetAsync_WhenHttpContextIsMissing_RejectsWithoutReadingCookies()
    {
        // Arrange
        _accessor.HttpContext = null;

        // Act
        var exception = await Record.ExceptionAsync(() => _provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GoogleAuthenticationFailedException>(exception);
        _externalAuthenticationServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenTicketIsInvalid_RejectsAuthentication()
    {
        // Arrange
        var httpContext = Assert.IsType<DefaultHttpContext>(_accessor.HttpContext);
        _externalAuthenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                httpContext,
                TestContext.Current.CancellationToken))
            .ReturnsAsync((GoogleExternalAuthenticationTicket?)null);

        // Act
        var exception = await Record.ExceptionAsync(() => _provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GoogleAuthenticationFailedException>(exception);
        _externalAuthenticationServiceMock.Verify(
            service => service.AuthenticateAsync(
                httpContext,
                TestContext.Current.CancellationToken),
            Times.Once);
        _externalAuthenticationServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenTicketExists_RequiresMatchingBrowserProof(bool matches)
    {
        // Arrange
        var httpContext = Assert.IsType<DefaultHttpContext>(_accessor.HttpContext);
        var context = TestFixture
            .Create()
            .Create<GoogleAuthenticationContext>();
        var ticket = new GoogleExternalAuthenticationTicket(
            context,
            "protected-binding");
        _externalAuthenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                httpContext,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ticket);
        _externalAuthenticationServiceMock
            .Setup(service => service.MatchesFlowBinding(
                ticket.FlowBinding,
                Flow))
            .Returns(matches);
        GoogleAuthenticationContext? actual = null;

        // Act
        var exception = await Record.ExceptionAsync(async () => actual = await _provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken));

        // Assert
        if (matches)
        {
            Assert.Null(exception);
            Assert.Same(
                context,
                actual);
        }
        else
        {
            Assert.IsType<GoogleFlowBindingMismatchException>(exception);
            Assert.Null(actual);
        }

        _externalAuthenticationServiceMock.Verify(
            service => service.AuthenticateAsync(
                httpContext,
                TestContext.Current.CancellationToken),
            Times.Once);
        _externalAuthenticationServiceMock.Verify(
            service => service.MatchesFlowBinding(
                ticket.FlowBinding,
                Flow),
            Times.Once);
        _externalAuthenticationServiceMock.VerifyNoOtherCalls();
    }
}
