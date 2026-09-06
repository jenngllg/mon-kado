using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpsertWishImageCommandHandlerTests
{
    private readonly Mock<IGiftImageProcessor> _processorMock;
    private readonly Mock<IGiftImageStore> _storeMock;
    private readonly Mock<IWishService> _wishServiceMock;
    private readonly UpsertWishImageCommandHandler _handler;

    public UpsertWishImageCommandHandlerTests()
    {
        _processorMock = new Mock<IGiftImageProcessor>(MockBehavior.Strict);
        _storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new UpsertWishImageCommandHandler(
            _processorMock.Object,
            _storeMock.Object,
            _wishServiceMock.Object,
            TimeProvider.System,
            NullLogger<UpsertWishImageCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenImageIsCommitted_ReturnsWishAndRemovesPendingMarker()
    {
        // Arrange
        var command = CreateCommand();
        var cancellationToken = TestContext.Current.CancellationToken;
        byte[] normalizedContent =
        [
            4,
            5,
            6
        ];
        byte[] contentHash =
        [
            7,
            8,
            9
        ];
        Guid? writtenImageId = null;
        _processorMock
            .Setup(processor => processor.ProcessAsync(
                command.Image,
                cancellationToken))
            .ReturnsAsync(new ProcessedGiftImage(
                normalizedContent,
                contentHash));
        _storeMock
            .Setup(store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                It.Is<ReadOnlyMemory<byte>>(content => content.ToArray().SequenceEqual(normalizedContent)),
                cancellationToken))
            .Callback<Guid, ReadOnlyMemory<byte>, CancellationToken>((imageId, _, _) => writtenImageId = imageId)
            .Returns(Task.CompletedTask);
        _wishServiceMock
            .Setup(service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                It.IsAny<Guid>(),
                contentHash,
                command.ExpectedVersion,
                cancellationToken))
            .ReturnsAsync(() => CreateWish(writtenImageId));
        _storeMock
            .Setup(store => store.MarkCommittedAsync(
                It.IsAny<Guid>(),
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.NotNull(writtenImageId);
        Assert.Equal(
            writtenImageId,
            result.ImageId);
        Assert.Equal(
            7,
            writtenImageId.Value.Version);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                command.Image,
                cancellationToken),
            Times.Once);
        _storeMock.Verify(
            store => store.WritePendingAsync(
                writtenImageId.Value,
                It.Is<ReadOnlyMemory<byte>>(content => content.ToArray().SequenceEqual(normalizedContent)),
                cancellationToken),
            Times.Once);
        _wishServiceMock.Verify(
            service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                writtenImageId.Value,
                contentHash,
                command.ExpectedVersion,
                cancellationToken),
            Times.Once);
        _storeMock.Verify(
            store => store.MarkCommittedAsync(
                writtenImageId.Value,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenNormalizedImageIsNotCommitted_DeletesUnusedPendingImage(
        bool hasCurrentImage)
    {
        // Arrange
        var command = CreateCommand();
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid? currentImageId = hasCurrentImage
            ? Guid.CreateVersion7()
            : null;
        Guid? writtenImageId = null;
        var processedImage = new ProcessedGiftImage(
            new byte[] { 1 },
            new byte[] { 2 });
        _processorMock
            .Setup(processor => processor.ProcessAsync(
                command.Image,
                cancellationToken))
            .ReturnsAsync(processedImage);
        _storeMock
            .Setup(store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                processedImage.Content,
                cancellationToken))
            .Callback<Guid, ReadOnlyMemory<byte>, CancellationToken>((imageId, _, _) => writtenImageId = imageId)
            .Returns(Task.CompletedTask);
        _wishServiceMock
            .Setup(service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                It.IsAny<Guid>(),
                processedImage.ContentHash,
                command.ExpectedVersion,
                cancellationToken))
            .ReturnsAsync(CreateWish(currentImageId));
        _storeMock
            .Setup(store => store.DeleteAsync(
                It.IsAny<Guid>(),
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.NotNull(writtenImageId);
        Assert.Equal(
            currentImageId,
            result.ImageId);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                command.Image,
                cancellationToken),
            Times.Once);
        _storeMock.Verify(
            store => store.WritePendingAsync(
                writtenImageId.Value,
                processedImage.Content,
                cancellationToken),
            Times.Once);
        _wishServiceMock.Verify(
            service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                writtenImageId.Value,
                processedImage.ContentHash,
                command.ExpectedVersion,
                cancellationToken),
            Times.Once);
        _storeMock.Verify(
            store => store.DeleteAsync(
                writtenImageId.Value,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenWishDoesNotExist_ThrowsWishNotFoundAndLeavesPendingMarkerForWorker()
    {
        // Arrange
        var command = CreateCommand();
        var cancellationToken = TestContext.Current.CancellationToken;
        var processedImage = new ProcessedGiftImage(
            new byte[] { 1 },
            new byte[] { 2 });
        _processorMock
            .Setup(processor => processor.ProcessAsync(
                command.Image,
                cancellationToken))
            .ReturnsAsync(processedImage);
        _storeMock
            .Setup(store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                processedImage.Content,
                cancellationToken))
            .Returns(Task.CompletedTask);
        _wishServiceMock
            .Setup(service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                It.IsAny<Guid>(),
                processedImage.ContentHash,
                command.ExpectedVersion,
                cancellationToken))
            .ReturnsAsync((WishDetails?)null);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishNotFoundException>(action);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                command.Image,
                cancellationToken),
            Times.Once);
        _storeMock.Verify(
            store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                processedImage.Content,
                cancellationToken),
            Times.Once);
        _wishServiceMock.Verify(
            service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                It.IsAny<Guid>(),
                processedImage.ContentHash,
                command.ExpectedVersion,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenPendingReconciliationFails_ReturnsCommittedDatabaseResult(
        bool wasCommitted)
    {
        // Arrange
        var command = CreateCommand();
        var cancellationToken = TestContext.Current.CancellationToken;
        var currentImageId = Guid.CreateVersion7();
        Guid? writtenImageId = null;
        var processedImage = new ProcessedGiftImage(
            new byte[] { 1 },
            new byte[] { 2 });
        _processorMock
            .Setup(processor => processor.ProcessAsync(
                command.Image,
                cancellationToken))
            .ReturnsAsync(processedImage);
        _storeMock
            .Setup(store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                processedImage.Content,
                cancellationToken))
            .Callback<Guid, ReadOnlyMemory<byte>, CancellationToken>((imageId, _, _) => writtenImageId = imageId)
            .Returns(Task.CompletedTask);
        _wishServiceMock
            .Setup(service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                It.IsAny<Guid>(),
                processedImage.ContentHash,
                command.ExpectedVersion,
                cancellationToken))
            .ReturnsAsync(() => CreateWish(wasCommitted
                ? writtenImageId
                : currentImageId));

        if (wasCommitted)
        {
            _storeMock
                .Setup(store => store.MarkCommittedAsync(
                    It.IsAny<Guid>(),
                    cancellationToken))
                .ThrowsAsync(new GiftImageStorageUnavailableException(new IOException()));
        }
        else
        {
            _storeMock
                .Setup(store => store.DeleteAsync(
                    It.IsAny<Guid>(),
                    cancellationToken))
                .ThrowsAsync(new GiftImageStorageUnavailableException(new IOException()));
        }

        // Act
        var result = await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        Assert.Equal(
            wasCommitted
                ? writtenImageId
                : currentImageId,
            result.ImageId);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                command.Image,
                cancellationToken),
            Times.Once);
        _storeMock.Verify(
            store => store.WritePendingAsync(
                It.IsAny<Guid>(),
                processedImage.Content,
                cancellationToken),
            Times.Once);
        _wishServiceMock.Verify(
            service => service.UpsertImageAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                It.IsAny<Guid>(),
                processedImage.ContentHash,
                command.ExpectedVersion,
                cancellationToken),
            Times.Once);

        if (wasCommitted)
        {
            _storeMock.Verify(
                store => store.MarkCommittedAsync(
                    It.IsAny<Guid>(),
                    cancellationToken),
                Times.Once);
        }
        else
        {
            _storeMock.Verify(
                store => store.DeleteAsync(
                    It.IsAny<Guid>(),
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private static UpsertWishImageCommand CreateCommand()
    {
        return new UpsertWishImageCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [1],
            42,
            true);
    }

    private static WishDetails CreateWish(Guid? imageId)
    {
        return new WishDetails(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Gift",
            null,
            null,
            null,
            1000,
            DateTime.UtcNow,
            null,
            42,
            1,
            imageId);
    }

    private void VerifyNoOtherCalls()
    {
        _processorMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
        _wishServiceMock.VerifyNoOtherCalls();
    }
}
