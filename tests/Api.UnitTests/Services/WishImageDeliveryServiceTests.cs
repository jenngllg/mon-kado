using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class WishImageDeliveryServiceTests
{
    private readonly Mock<IWishImageAccessService> _accessServiceMock;
    private readonly Mock<IGiftImageStore> _storeMock;
    private readonly WishImageDeliveryService _service;

    public WishImageDeliveryServiceTests()
    {
        _accessServiceMock = new Mock<IWishImageAccessService>(MockBehavior.Strict);
        _storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        _service = new WishImageDeliveryService(
            _accessServiceMock.Object,
            _storeMock.Object);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenAsync_WhenGrantIsCurrent_ReturnsImageStream(bool isOwned)
    {
        // Arrange
        var grant = CreateGrant(isOwned);
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedStream = new MemoryStream([1]);

        if (isOwned)
        {
            _accessServiceMock
                .Setup(service => service.IsOwnedImageCurrentAsync(
                    grant.OwnerId.GetValueOrDefault(),
                    grant.WishlistId,
                    grant.WishId,
                    grant.ImageId,
                    cancellationToken))
                .ReturnsAsync(true);
        }
        else
        {
            _accessServiceMock
                .Setup(service => service.IsSharedImageCurrentAsync(
                    grant.ShareLinkId.GetValueOrDefault(),
                    grant.WishlistId,
                    grant.WishId,
                    grant.ImageId,
                    cancellationToken))
                .ReturnsAsync(true);
        }

        _storeMock
            .Setup(store => store.OpenReadAsync(
                grant.ImageId,
                cancellationToken))
            .ReturnsAsync(expectedStream);

        // Act
        var result = isOwned
            ? await _service.OpenOwnedAsync(
                grant,
                cancellationToken)
            : await _service.OpenSharedAsync(
                grant,
                cancellationToken);

        // Assert
        Assert.Same(
            expectedStream,
            result);
        VerifyAccess(grant, isOwned, cancellationToken);
        _storeMock.Verify(
            store => store.OpenReadAsync(
                grant.ImageId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
        await result.DisposeAsync();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task OpenAsync_WhenGrantIsNotCurrentOrFileIsMissing_ThrowsExpectedException(
        bool isOwned,
        bool isCurrent)
    {
        // Arrange
        var grant = CreateGrant(isOwned);
        var cancellationToken = TestContext.Current.CancellationToken;

        if (isOwned)
        {
            _accessServiceMock
                .Setup(service => service.IsOwnedImageCurrentAsync(
                    grant.OwnerId.GetValueOrDefault(),
                    grant.WishlistId,
                    grant.WishId,
                    grant.ImageId,
                    cancellationToken))
                .ReturnsAsync(isCurrent);
        }
        else
        {
            _accessServiceMock
                .Setup(service => service.IsSharedImageCurrentAsync(
                    grant.ShareLinkId.GetValueOrDefault(),
                    grant.WishlistId,
                    grant.WishId,
                    grant.ImageId,
                    cancellationToken))
                .ReturnsAsync(isCurrent);
        }

        if (isCurrent)
        {
            _storeMock
                .Setup(store => store.OpenReadAsync(
                    grant.ImageId,
                    cancellationToken))
                .ReturnsAsync((Stream?)null);
        }

        // Act
        var action = () => isOwned
            ? _service.OpenOwnedAsync(
                grant,
                cancellationToken)
            : _service.OpenSharedAsync(
                grant,
                cancellationToken);

        // Assert
        var exception = await Record.ExceptionAsync(action);
        Assert.IsType(
            isCurrent
                ? typeof(GiftImageStorageUnavailableException)
                : typeof(GiftImageNotFoundException),
            exception);
        VerifyAccess(grant, isOwned, cancellationToken);

        if (isCurrent)
        {
            _storeMock.Verify(
                store => store.OpenReadAsync(
                    grant.ImageId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenAsync_WhenGrantIdentifierIsMissing_ThrowsNotFoundWithoutDependencies(
        bool isOwned)
    {
        // Arrange
        var grant = CreateGrant(isOwned);
        grant = new WishImageGrant
        {
            WishlistId = grant.WishlistId,
            WishId = grant.WishId,
            ImageId = grant.ImageId
        };

        // Act
        var action = () => isOwned
            ? _service.OpenOwnedAsync(
                grant,
                TestContext.Current.CancellationToken)
            : _service.OpenSharedAsync(
                grant,
                TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GiftImageNotFoundException>(action);
        VerifyNoOtherCalls();
    }

    private static WishImageGrant CreateGrant(bool isOwned)
    {
        return new WishImageGrant
        {
            OwnerId = isOwned
                ? Guid.CreateVersion7()
                : null,
            ShareLinkId = isOwned
                ? null
                : Guid.CreateVersion7(),
            WishlistId = Guid.CreateVersion7(),
            WishId = Guid.CreateVersion7(),
            ImageId = Guid.CreateVersion7()
        };
    }

    private void VerifyAccess(
        WishImageGrant grant,
        bool isOwned,
        CancellationToken cancellationToken)
    {
        if (isOwned)
        {
            _accessServiceMock.Verify(
                service => service.IsOwnedImageCurrentAsync(
                    grant.OwnerId.GetValueOrDefault(),
                    grant.WishlistId,
                    grant.WishId,
                    grant.ImageId,
                    cancellationToken),
                Times.Once);

            return;
        }

        _accessServiceMock.Verify(
            service => service.IsSharedImageCurrentAsync(
                grant.ShareLinkId.GetValueOrDefault(),
                grant.WishlistId,
                grant.WishId,
                grant.ImageId,
                cancellationToken),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _accessServiceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
    }
}
