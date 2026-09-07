using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using MediatR;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class LinkGoogleSessionCommandHandlerTests
{
    private const string Flow = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Password = " exact-current-password ";
    private readonly Mock<IGoogleAuthenticationContextProvider> _contextProviderMock;
    private readonly Mock<ISender> _senderMock;
    private readonly LinkGoogleSessionCommandHandler _handler;
    public LinkGoogleSessionCommandHandlerTests()
    {
        _contextProviderMock = new Mock<IGoogleAuthenticationContextProvider>(MockBehavior.Strict);
        _senderMock = new Mock<ISender>(MockBehavior.Strict);
        _handler = new LinkGoogleSessionCommandHandler(
            _contextProviderMock.Object,
            _senderMock.Object);
    }

    [Fact]
    public async Task Handle_WhenBrowserProofIsValid_ForwardsProtectedContextAndExactPassword()
    {
        // Arrange
        var fixture = TestFixture.Create();
        var context = fixture.Create<GoogleAuthenticationContext>();
        var expected = fixture.Create<AccountSessionTokens>();
        _contextProviderMock
            .Setup(provider => provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(context);
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<LinkGoogleAccountCommand>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            new LinkGoogleSessionCommand(
                Flow,
                Password),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _contextProviderMock.Verify(
            provider => provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken),
            Times.Once);
        _senderMock.Verify(
            sender => sender.Send(
                It.Is<LinkGoogleAccountCommand>(command => command.Identity == context.Identity && command.ReturnPath == context.ReturnPath && command.RememberMe == context.IsPersistent && command.FlowId == context.FlowId && command.ExpectedMemberId == context.ExpectedMemberId && command.CurrentSessionId == context.CurrentSessionId && command.CurrentPassword == Password),
                TestContext.Current.CancellationToken),
            Times.Once);
        _contextProviderMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenBindingBelongsToAnotherFlow_ReturnsGenericLinkFailure()
    {
        // Arrange
        _contextProviderMock
            .Setup(provider => provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new GoogleFlowBindingMismatchException());

        // Act
        var exception = await Record.ExceptionAsync(() => _handler.Handle(
                new LinkGoogleSessionCommand(
                    Flow,
                    Password),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GoogleAccountLinkFailedException>(exception);
        _contextProviderMock.Verify(
            provider => provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken),
            Times.Once);
        _contextProviderMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }
}
