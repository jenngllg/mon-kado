using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateMemberProfileCommandHandlerTests
{
    private readonly UpdateMemberProfileCommandHandler _handler;
    private readonly Mock<IMemberProfileService> _memberProfileServiceMock;

    public UpdateMemberProfileCommandHandlerTests()
    {
        _memberProfileServiceMock = new Mock<IMemberProfileService>(MockBehavior.Strict);
        _handler = new UpdateMemberProfileCommandHandler(
            _memberProfileServiceMock.Object,
            NullLogger<UpdateMemberProfileCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenMemberExists_ReturnsUpdatedProfile()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpdateMemberProfileCommand(
            memberId,
            " Jenn ",
            42);
        var expected = new MemberProfile(
            "Jenn",
            43);
        _memberProfileServiceMock
            .Setup(service => service.UpdateAsync(
                memberId,
                "Jenn",
                42,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _memberProfileServiceMock.Verify(
            service => service.UpdateAsync(
                memberId,
                "Jenn",
                42,
                cancellationToken),
            Times.Once);
        _memberProfileServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new UpdateMemberProfileCommand(
            memberId,
            null,
            42);
        _memberProfileServiceMock
            .Setup(service => service.UpdateAsync(
                memberId,
                string.Empty,
                42,
                cancellationToken))
            .ReturnsAsync((MemberProfile?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _memberProfileServiceMock.Verify(
            service => service.UpdateAsync(
                memberId,
                string.Empty,
                42,
                cancellationToken),
            Times.Once);
        _memberProfileServiceMock.VerifyNoOtherCalls();
    }
}
