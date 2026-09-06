using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class DeleteProfileImageCommandHandlerTests
{
    private readonly Mock<IProfileImageService> _profileImageServiceMock;
    private readonly DeleteProfileImageCommandHandler _handler;
    public DeleteProfileImageCommandHandlerTests()
    {
        _profileImageServiceMock = new Mock<IProfileImageService>(MockBehavior.Strict);
        _handler = new DeleteProfileImageCommandHandler(
            _profileImageServiceMock.Object,
            NullLogger<DeleteProfileImageCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenDeletionSucceeds_ReturnsUpdatedVersionAndForwardsCancellation()
    {
        // Arrange
        var profile = TestFixture
            .Create()
            .Create<MemberProfile>();
        var request = new DeleteProfileImageCommand(
            Guid.CreateVersion7(),
            42);
        var token = TestContext.Current.CancellationToken;
        _profileImageServiceMock
            .Setup(service => service.DeleteAsync(
                request.MemberId,
                request.ExpectedVersion,
                token))
            .ReturnsAsync(profile);

        // Act
        var version = await _handler.Handle(
            request,
            token);

        // Assert
        Assert.Equal(
            profile.Version,
            version);
        _profileImageServiceMock.Verify(
            service => service.DeleteAsync(
                request.MemberId,
                request.ExpectedVersion,
                token),
            Times.Once);
        _profileImageServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenPhotoIsAbsent_PreservesExpectedException()
    {
        // Arrange
        var request = new DeleteProfileImageCommand(
            Guid.CreateVersion7(),
            42);
        var token = TestContext.Current.CancellationToken;
        var expected = new ProfileImageNotFoundException();
        _profileImageServiceMock
            .Setup(service => service.DeleteAsync(
                request.MemberId,
                request.ExpectedVersion,
                token))
            .ThrowsAsync(expected);

        // Act
        var exception = await Assert.ThrowsAsync<ProfileImageNotFoundException>(() => _handler.Handle(
                request,
                token));

        // Assert
        Assert.Same(
            expected,
            exception);
        _profileImageServiceMock.Verify(
            service => service.DeleteAsync(
                request.MemberId,
                request.ExpectedVersion,
                token),
            Times.Once);
        _profileImageServiceMock.VerifyNoOtherCalls();
    }
}
