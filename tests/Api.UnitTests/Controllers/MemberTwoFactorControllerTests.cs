using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using MediatR;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

public class MemberTwoFactorControllerTests
{
    private readonly Mock<ISender> _senderMock = new(MockBehavior.Strict);
    private readonly Mock<ITwoFactorCallerProvider> _callerProviderMock = new(MockBehavior.Strict);
    private readonly MemberTwoFactorController _controller;

    public MemberTwoFactorControllerTests()
    {
        _controller = new MemberTwoFactorController(
            _senderMock.Object,
            _callerProviderMock.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenValidatedCallerIsUnavailable_RejectsBeforeSendingCommand(bool reauthenticate)
    {
        // Arrange
        _callerProviderMock
            .Setup(provider => provider.GetCurrent())
            .Returns((JennGllg.Fr.MonKado.Back.Application.Models.TwoFactorCaller?)null);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {

            if (reauthenticate)
                await _controller.ReauthenticateAsync(
                    new TwoFactorReauthenticationRequest(),
                    cancellationToken);
            else
                await _controller.GetStatusAsync(cancellationToken);
        });

        // Assert
        Assert.IsType<InvalidAccessTokenException>(exception);
        _callerProviderMock.Verify(
            provider => provider.GetCurrent(),
            Times.Once);
        _callerProviderMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }
}
