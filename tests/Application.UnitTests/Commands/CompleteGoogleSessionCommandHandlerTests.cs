using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using MediatR;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CompleteGoogleSessionCommandHandlerTests
{
    private const string Flow = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly Mock<IGoogleAuthenticationContextProvider> _contextProviderMock;
    private readonly Mock<ISender> _senderMock;
    private readonly CompleteGoogleSessionCommandHandler _handler;
    private readonly GoogleAuthenticationContext _context;
    public CompleteGoogleSessionCommandHandlerTests()
    {
        _contextProviderMock = new Mock<IGoogleAuthenticationContextProvider>(MockBehavior.Strict);
        _senderMock = new Mock<ISender>(MockBehavior.Strict);
        _handler = new CompleteGoogleSessionCommandHandler(
            _contextProviderMock.Object,
            _senderMock.Object);
        _context = TestFixture
            .Create()
            .Create<GoogleAuthenticationContext>();
    }

    [Fact]
    public async Task Handle_WhenSessionIsConfirmed_IssuesJwtForCommittedMemberAndForwardsContext()
    {
        // Arrange
        var fixture = TestFixture.Create();
        var memberId = Guid.CreateVersion7();
        var refresh = fixture.Create<AccountRefreshSession>();
        var accessToken = fixture.Create<AccessToken>();
        ConfigureResult(new GoogleAuthenticationResult(
                GoogleAuthenticationOutcome.SessionCreated,
                refresh,
                memberId)
        {
            AccessToken = accessToken
        });

        // Act
        var result = await _handler.Handle(
            new CompleteGoogleSessionCommand(Flow),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(
            accessToken,
            result.AccessToken);
        Assert.Equal(
            refresh.RefreshToken,
            result.RefreshToken);
        Assert.Equal(
            refresh.RefreshTokenExpiresAt,
            result.RefreshTokenExpiresAt);
        Assert.Equal(
            refresh.IsPersistent,
            result.IsPersistent);
        VerifyContext();
    }

    [Theory]
    [InlineData(GoogleAuthenticationOutcome.ExplicitLinkRequired, typeof(GoogleAccountLinkRequiredException))]
    [InlineData(GoogleAuthenticationOutcome.AdditionalVerificationRequired, typeof(GoogleAdditionalVerificationRequiredException))]
    [InlineData((GoogleAuthenticationOutcome)99, typeof(GoogleAuthenticationFailedException))]
    public async Task Handle_WhenOutcomeDoesNotCreateSession_ThrowsWithoutIssuingJwt(
        GoogleAuthenticationOutcome outcome,
        Type expectedException)
    {
        // Arrange
        ConfigureResult(new GoogleAuthenticationResult(
                outcome,
                null));

        // Act
        var exception = await Record.ExceptionAsync(() => _handler.Handle(
                new CompleteGoogleSessionCommand(Flow),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        VerifyContext();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("member")]
    [InlineData("emptyMember")]
    [InlineData("accessToken")]
    public async Task Handle_WhenSuccessResultIsIncomplete_RejectsWithoutIssuingJwt(string missing)
    {
        // Arrange
        var session = missing == "session" ? null : TestFixture
            .Create()
            .Create<AccountRefreshSession>();
        Guid? memberId = missing == "member" ? null : Guid.CreateVersion7();

        if (missing == "emptyMember")
            memberId = Guid.Empty;
        ConfigureResult(new GoogleAuthenticationResult(
                GoogleAuthenticationOutcome.SessionCreated,
                session,
                memberId)
        {
            AccessToken = missing == "accessToken" ? null : TestFixture.Create().Create<AccessToken>()
        });

        // Act
        var exception = await Record.ExceptionAsync(() => _handler.Handle(
                new CompleteGoogleSessionCommand(Flow),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GoogleAuthenticationFailedException>(exception);
        VerifyContext();
    }

    private void ConfigureResult(GoogleAuthenticationResult result)
    {
        _contextProviderMock
            .Setup(provider => provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(_context);
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<CompleteGoogleAuthenticationCommand>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(result);
    }

    private void VerifyContext()
    {
        _contextProviderMock.Verify(
            provider => provider.GetAsync(
                Flow,
                TestContext.Current.CancellationToken),
            Times.Once);
        _senderMock.Verify(
            sender => sender.Send(
                It.Is<CompleteGoogleAuthenticationCommand>(command => command.Identity == _context.Identity && command.ReturnPath == _context.ReturnPath && command.RememberMe == _context.IsPersistent && command.FlowId == _context.FlowId && command.ExpectedMemberId == _context.ExpectedMemberId && command.CurrentSessionId == _context.CurrentSessionId),
                TestContext.Current.CancellationToken),
            Times.Once);
        _contextProviderMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }
}
