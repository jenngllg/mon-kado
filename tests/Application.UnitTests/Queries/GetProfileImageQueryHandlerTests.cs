using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetProfileImageQueryHandlerTests
{
    private readonly Mock<IProfileImageService> _profileImageServiceMock;
    private readonly Mock<IGiftImageStore> _storeMock;
    private readonly GetProfileImageQueryHandler _handler;
    public GetProfileImageQueryHandlerTests()
    {
        _profileImageServiceMock = new Mock<IProfileImageService>(MockBehavior.Strict);
        _storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        _handler = new GetProfileImageQueryHandler(
            _profileImageServiceMock.Object,
            _storeMock.Object,
            NullLogger<GetProfileImageQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenImageIsCurrent_OpensOnlyTheValidatedReference(bool fileExists)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        var request = new GetProfileImageQuery(
            memberId,
            imageId);
        var token = TestContext.Current.CancellationToken;
        using var stream = new MemoryStream();
        _profileImageServiceMock
            .Setup(service => service.IsCurrentAsync(
                memberId,
                imageId,
                token))
            .ReturnsAsync(true);
        _storeMock
            .Setup(store => store.OpenReadAsync(
                imageId,
                token))
            .ReturnsAsync(fileExists ? stream : null);

        // Act
        var action = () => _handler.Handle(
            request,
            token);

        // Assert
        if (fileExists)
            Assert.Same(
                stream,
                await action());
        else
            await Assert.ThrowsAsync<GiftImageStorageUnavailableException>(action);
        _profileImageServiceMock.Verify(
            service => service.IsCurrentAsync(
                memberId,
                imageId,
                token),
            Times.Once);
        _storeMock.Verify(
            store => store.OpenReadAsync(
                imageId,
                token),
            Times.Once);
        _profileImageServiceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenReferenceIsObsolete_DoesNotReadStorage()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        var token = TestContext.Current.CancellationToken;
        _profileImageServiceMock
            .Setup(service => service.IsCurrentAsync(
                memberId,
                imageId,
                token))
            .ReturnsAsync(false);

        // Act
        await Assert.ThrowsAsync<ProfileImageNotFoundException>(() => _handler.Handle(
                new GetProfileImageQuery(
                    memberId,
                    imageId),
                token));

        // Assert
        _profileImageServiceMock.Verify(
            service => service.IsCurrentAsync(
                memberId,
                imageId,
                token),
            Times.Once);
        _profileImageServiceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
    }
}
