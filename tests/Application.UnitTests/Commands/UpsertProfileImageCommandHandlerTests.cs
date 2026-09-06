using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpsertProfileImageCommandHandlerTests
{
    private readonly Mock<IProfileImageProcessor> _processorMock;
    private readonly Mock<IGiftImageStore> _storeMock;
    private readonly Mock<IProfileImageService> _profileImageServiceMock;
    private readonly RecordingExceptionLogger<UpsertProfileImageCommandHandler> _logger = new();
    private readonly UpsertProfileImageCommandHandler _handler;
    public UpsertProfileImageCommandHandlerTests()
    {
        _processorMock = new Mock<IProfileImageProcessor>(MockBehavior.Strict);
        _storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        _profileImageServiceMock = new Mock<IProfileImageService>(MockBehavior.Strict);
        _handler = new UpsertProfileImageCommandHandler(
            _processorMock.Object,
            _storeMock.Object,
            _profileImageServiceMock.Object,
            TimeProvider.System,
            _logger);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Handle_WhenCommitIsConfirmed_ReconcilesWithoutLeakingStorageExceptions(
        bool usesNewImage,
        bool reconciliationFails)
    {
        // Arrange
        var command = new UpsertProfileImageCommand(
            Guid.CreateVersion7(),
            [1],
            42,
            true);
        var token = TestContext.Current.CancellationToken;
        var processed = new ProcessedGiftImage(
            new byte[] { 2 },
            new byte[32]);
        var writtenId = Guid.Empty;
        var previousId = Guid.CreateVersion7();
        _processorMock
            .Setup(processor => processor.ProcessAsync(
                command.Image,
                token))
            .ReturnsAsync(processed);
        _storeMock
            .Setup(store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                processed.Content,
                token))
            .Callback<Guid, ReadOnlyMemory<byte>, CancellationToken>((
                id,
                _,
                _) => writtenId = id)
            .Returns(Task.CompletedTask);
        _profileImageServiceMock
            .Setup(service => service.UpsertAsync(
                command.MemberId,
                It.IsAny<Guid>(),
                processed.ContentHash,
                command.ExpectedVersion,
                token))
            .ReturnsAsync(() => new MemberProfile(
                "Jennifer",
                43)
            {
                ProfileImageId = usesNewImage ? writtenId : previousId
            });
        var completion = usesNewImage ? _storeMock.Setup(store => store.MarkCommittedAsync(
                It.IsAny<Guid>(),
                token)) : _storeMock.Setup(store => store.DeleteAsync(
                It.IsAny<Guid>(),
                token));

        if (reconciliationFails)
            completion.ThrowsAsync(new GiftImageStorageUnavailableException(new IOException("private/storage/path")));
        else
            completion.Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(
            command,
            token);

        // Assert
        Assert.Equal(
            usesNewImage ? writtenId : previousId,
            result.ProfileImageId);
        Assert.Equal(
            7,
            writtenId.Version);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                command.Image,
                token),
            Times.Once);
        _storeMock.Verify(
            store => store.WritePendingAsync(
                writtenId,
                processed.Content,
                token),
            Times.Once);
        _profileImageServiceMock.Verify(
            service => service.UpsertAsync(
                command.MemberId,
                writtenId,
                processed.ContentHash,
                command.ExpectedVersion,
                token),
            Times.Once);

        if (usesNewImage)
            _storeMock.Verify(
                store => store.MarkCommittedAsync(
                    writtenId,
                    token),
                Times.Once);
        else
            _storeMock.Verify(
                store => store.DeleteAsync(
                    writtenId,
                    token),
                Times.Once);
        Assert.DoesNotContain(
            _logger.Entries,
            entry => entry.Contains(
                "private/storage/path",
                StringComparison.Ordinal));
        _processorMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
        _profileImageServiceMock.VerifyNoOtherCalls();
    }
}
