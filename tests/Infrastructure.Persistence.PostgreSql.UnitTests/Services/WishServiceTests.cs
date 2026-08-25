using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

using Npgsql;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishServiceTests
{
    private const string WishlistForeignKeyName = "fk_wishes_wishlists_wishlist_id";
    private const string PositionWishlistForeignKeyName = "fk_wish_position_sequences_wishlists_wishlist_id";

    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IWishTransactionFactory> _wishTransactionFactoryMock;
    private readonly Mock<IWishlistRepository> _wishlistRepositoryMock;
    private readonly Mock<IWishRepository> _wishRepositoryMock;
    private readonly WishService _wishService;

    public WishServiceTests()
    {
        _wishRepositoryMock = new Mock<IWishRepository>(MockBehavior.Strict);
        _wishlistRepositoryMock = new Mock<IWishlistRepository>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _wishTransactionFactoryMock = new Mock<IWishTransactionFactory>(MockBehavior.Strict);
        _wishService = new WishService(
            _wishRepositoryMock.Object,
            _wishlistRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _wishTransactionFactoryMock.Object);
    }

    [Fact]
    public async Task GetCollectionAsync_WhenCollectionExists_ReturnsConsistentOrderedSnapshot()
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        var firstWish = CreateWish(data);
        var secondWish = new Wish(
            Guid.CreateVersion7(),
            data.WishlistId,
            "Livre",
            null,
            null,
            null,
            3);
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken))
            .ReturnsAsync(transactionMock.Object);
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(CreateSequence(data.WishlistId));
        _wishRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync([
                firstWish,
                secondWish
            ]);
        transactionMock
            .Setup(transaction => transaction.CommitAsync(data.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _wishService.GetCollectionAsync(
            data.OwnerId,
            data.WishlistId,
            data.CancellationToken);

        // Assert
        Assert.Equal(
            42u,
            result.Version);
        Assert.Equal(
            [firstWish.Id, secondWish.Id],
            result.Wishes.Select(wish => wish.Id));
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByWishlistIdAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        transactionMock.Verify(
            transaction => transaction.CommitAsync(data.CancellationToken),
            Times.Once);
        transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.NotOwned, typeof(WishlistNotFoundException))]
    public async Task GetCollectionAsync_WhenWishlistIsUnavailable_ThrowsExpectedException(
        WishlistAccess access,
        Type expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);

        // Act
        var action = () => _wishService.GetCollectionAsync(
            data.OwnerId,
            data.WishlistId,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync(
            expectedExceptionType,
            action);
        VerifyOwnedAccess(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetCollectionAsync_WhenSequenceIsMissing_ThrowsWishlistNotFoundException()
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken))
            .ReturnsAsync(transactionMock.Object);
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync((WishPositionSequence?)null);

        // Act
        var action = () => _wishService.GetCollectionAsync(
            data.OwnerId,
            data.WishlistId,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetCollectionAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishService.GetCollectionAsync(
            data.OwnerId,
            data.WishlistId,
            data.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReorderAsync_WhenOrderChanges_ReusesExistingPositionsAndReturnsUpdatedOrder()
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        var firstWish = CreateWish(data);
        var secondWish = new Wish(
            Guid.CreateVersion7(),
            data.WishlistId,
            "Livre",
            null,
            null,
            null,
            3);
        var thirdWish = new Wish(
            Guid.CreateVersion7(),
            data.WishlistId,
            "Jeu",
            null,
            null,
            null,
            4);
        var sequence = CreateSequence(data.WishlistId);
        Guid[] requestedOrder =
        [
            firstWish.Id,
            thirdWish.Id,
            secondWish.Id
        ];
        SetupReorder(
            data,
            transactionMock,
            sequence,
            [
                firstWish,
                secondWish,
                thirdWish
            ]);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ReturnsAsync(2);
        _wishRepositoryMock
            .Setup(repository => repository.ReloadCollectionStateAsync(
                sequence,
                data.CancellationToken))
            .Returns(Task.CompletedTask);
        transactionMock
            .Setup(transaction => transaction.CommitAsync(data.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            requestedOrder,
            42,
            data.CancellationToken);

        // Assert
        Assert.Equal(
            requestedOrder,
            result.Wishes.Select(wish => wish.Id));
        Assert.Equal(
            [1L, 3L, 4L],
            result.Wishes.Select(wish => wish.Position));
        Assert.Equal(
            3,
            thirdWish.Position);
        Assert.Equal(
            4,
            secondWish.Position);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Exactly(2));
        _wishRepositoryMock.Verify(
            repository => repository.ReloadCollectionStateAsync(
                sequence,
                data.CancellationToken),
            Times.Once);
        transactionMock.Verify(
            transaction => transaction.CommitAsync(data.CancellationToken),
            Times.Once);
        VerifyReorderSetup(
            data,
            transactionMock);
    }

    [Fact]
    public async Task ReorderAsync_WhenOrderIsUnchanged_ReturnsWithoutWriting()
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        var wish = CreateWish(data);
        SetupReorder(
            data,
            transactionMock,
            CreateSequence(data.WishlistId),
            [wish]);

        // Act
        var result = await _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            [wish.Id],
            42,
            data.CancellationToken);

        // Assert
        Assert.Equal(
            42u,
            result.Version);
        Assert.Equal(
            wish.Id,
            Assert.Single(result.Wishes).Id);
        VerifyReorderSetup(
            data,
            transactionMock);
    }

    [Fact]
    public async Task ReorderAsync_WhenVersionIsStale_ThrowsWishOrderVersionConflictException()
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken))
            .ReturnsAsync(transactionMock.Object);
        _wishRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync([]);
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(CreateSequence(data.WishlistId));

        // Act
        var action = () => _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            [],
            41,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishOrderVersionConflictException>(action);
        transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReorderAsync_WhenSequenceIsMissing_ThrowsWishlistNotFoundException()
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken))
            .ReturnsAsync(transactionMock.Object);
        _wishRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync([]);
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync((WishPositionSequence?)null);

        // Act
        var action = () => _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            [],
            42,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNotFoundException>(action);
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReorderAsync_WhenMembershipDiffers_ThrowsWishOrderConflictException(
        bool countDiffers)
    {
        // Arrange
        var data = CreateData();
        var transactionMock = CreateTransactionMock();
        var wish = CreateWish(data);
        SetupReorder(
            data,
            transactionMock,
            CreateSequence(data.WishlistId),
            [wish]);
        IReadOnlyCollection<Guid> requestedOrder = countDiffers
            ? []
            : [Guid.CreateVersion7()];

        // Act
        var action = () => _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            requestedOrder,
            42,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishOrderConflictException>(action);
        VerifyReorderSetup(
            data,
            transactionMock);
    }

    [Fact]
    public async Task ReorderAsync_WhenPostgreSqlFailsBeforeCommit_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());
        _wishRepositoryMock
            .Setup(repository => repository.ClearTracking());

        // Act
        var action = () => _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            [],
            42,
            data.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.ClearTracking(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, typeof(DependencyUnavailableException))]
    [InlineData(false, false, typeof(WishOrderVersionConflictException))]
    public async Task ReorderAsync_WhenCommitAcknowledgementIsLost_ResolvesPersistedOrder(
        bool matchesRequestedOrder,
        bool matchesOriginalOrder,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        var reorderTransactionMock = CreateTransactionMock();
        var verificationTransactionMock = CreateTransactionMock();
        var firstWish = CreateWish(
            Guid.CreateVersion7(),
            data.WishlistId,
            "Premier",
            1);
        var secondWish = CreateWish(
            Guid.CreateVersion7(),
            data.WishlistId,
            "Deuxième",
            3);
        var thirdWish = CreateWish(
            Guid.CreateVersion7(),
            data.WishlistId,
            "Troisième",
            4);
        Wish[] originalWishes =
        [
            firstWish,
            secondWish,
            thirdWish
        ];
        Guid[] requestedOrder =
        [
            thirdWish.Id,
            firstWish.Id,
            secondWish.Id
        ];
        var persistedWishes = matchesRequestedOrder
            ? new[]
            {
                thirdWish,
                firstWish,
                secondWish
            }
            : CreatePersistedOrder(
                data.WishlistId,
                matchesOriginalOrder
                    ? [firstWish.Id, secondWish.Id, thirdWish.Id]
                    : [secondWish.Id, thirdWish.Id, firstWish.Id]);
        var sequence = CreateSequence(data.WishlistId);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken))
            .ReturnsAsync(reorderTransactionMock.Object);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken))
            .ReturnsAsync(verificationTransactionMock.Object);
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(sequence);
        _wishRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(originalWishes);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ReturnsAsync(2);
        _wishRepositoryMock
            .Setup(repository => repository.ReloadCollectionStateAsync(
                sequence,
                data.CancellationToken))
            .Returns(Task.CompletedTask);
        reorderTransactionMock
            .Setup(transaction => transaction.CommitAsync(data.CancellationToken))
            .ThrowsAsync(new TimeoutException());
        _wishRepositoryMock
            .Setup(repository => repository.ClearTracking());
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(sequence);
        _wishRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(persistedWishes);
        verificationTransactionMock
            .Setup(transaction => transaction.CommitAsync(data.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        WishOrderDetails? result = null;
        var action = async () => result = await _wishService.ReorderAsync(
            data.OwnerId,
            data.WishlistId,
            requestedOrder,
            42,
            data.CancellationToken);

        // Assert
        if (expectedExceptionType is null)
        {
            await action();
            Assert.NotNull(result);
            Assert.Equal(
                requestedOrder,
                result.Wishes.Select(wish => wish.Id));
        }
        else
        {
            await Assert.ThrowsAsync(
                expectedExceptionType,
                action);
        }

        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Exactly(2));
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken),
            Times.Once);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Exactly(2));
        _wishRepositoryMock.Verify(
            repository => repository.ReloadCollectionStateAsync(
                sequence,
                data.CancellationToken),
            Times.Once);
        reorderTransactionMock.Verify(
            transaction => transaction.CommitAsync(data.CancellationToken),
            Times.Once);
        reorderTransactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        reorderTransactionMock.VerifyNoOtherCalls();
        _wishRepositoryMock.Verify(
            repository => repository.ClearTracking(),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByWishlistIdAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        verificationTransactionMock.Verify(
            transaction => transaction.CommitAsync(data.CancellationToken),
            Times.Once);
        verificationTransactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        verificationTransactionMock.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_WhenWishLimitIsReached_ThrowsWishLimitReachedException(
        bool directException)
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(1001);
        _wishRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wish>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ThrowsAsync(CreateWishLimitException(directException));

        // Act
        var action = () => CreateAsync(data);

        // Assert
        await Assert.ThrowsAsync<WishLimitReachedException>(action);
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wish>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenSaveSucceeds_ReturnsMappedWish()
    {
        // Arrange
        var data = CreateData();
        Wish? addedWish = null;
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(3);
        _wishRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wish>()))
            .Callback<Wish>(wish => addedWish = wish);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await CreateAsync(data);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(addedWish);
        Assert.Equal(
            data.Id,
            result.Id);
        Assert.Equal(
            data.WishlistId,
            result.WishlistId);
        Assert.Equal(
            data.Name,
            result.Name);
        Assert.Equal(
            data.Note,
            result.Note);
        Assert.Equal(
            data.Url,
            result.Url);
        Assert.Equal(
            data.Price,
            result.Price);
        Assert.Equal(
            3,
            result.Position);
        VerifyCreation(
            data,
            addedWish);
    }

    [Theory]
    [InlineData(WishlistForeignKeyName, false)]
    [InlineData(PositionWishlistForeignKeyName, true)]
    public async Task CreateAsync_WhenParentForeignKeyFailsAndMemberExists_ReturnsNull(
        string constraintName,
        bool directException)
    {
        // Arrange
        var data = CreateData();
        var exception = CreateForeignKeyException(
            constraintName,
            directException);
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(exception);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        var result = await CreateAsync(data);

        // Assert
        Assert.Null(result);
        VerifyAllocationAndAccess(data);
    }

    [Fact]
    public async Task CreateAsync_WhenParentDisappearsWithMember_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(CreateForeignKeyException(
                PositionWishlistForeignKeyName,
                directException: false));
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(WishlistAccess.MemberNotFound);

        // Act
        var action = () => CreateAsync(data);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        VerifyAllocationAndAccess(data);
    }

    [Fact]
    public async Task CreateAsync_WhenPositionAllocationTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => CreateAsync(data);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenCommitAcknowledgementIsLostAndWishMatches_ReturnsCommittedWish()
    {
        // Arrange
        var data = CreateData();
        var committedWish = ConfigureAmbiguousCreation(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(committedWish);

        // Act
        var result = await CreateAsync(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            data.Id,
            result.Id);
        VerifyAmbiguousCreation(
            data,
            committedWish,
            verifiesAccess: false);
    }

    [Theory]
    [InlineData(WishlistAccess.NotOwned, null)]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.Owner, typeof(DependencyUnavailableException))]
    public async Task CreateAsync_WhenAmbiguousCreationCannotBeConfirmed_ResolvesAccess(
        WishlistAccess access,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureAmbiguousCreation(data);
        var conflictingWish = new Wish(
            Guid.CreateVersion7(),
            data.WishlistId,
            data.Name,
            data.Note,
            data.Url,
            data.Price,
            1);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(conflictingWish);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);

        // Act
        var action = () => CreateAsync(data);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.Null(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyAmbiguousCreation(
            data,
            attemptedWish,
            verifiesAccess: true);
    }

    [Fact]
    public async Task CreateAsync_WhenAmbiguousCreationLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureAmbiguousCreation(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => CreateAsync(data);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyAmbiguousCreation(
            data,
            attemptedWish,
            verifiesAccess: false);
    }

    [Fact]
    public async Task CreateAsync_WhenMissingParentAccessCheckTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(CreateForeignKeyException(
                PositionWishlistForeignKeyName,
                directException: true));
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => CreateAsync(data);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyAllocationAndAccess(data);
    }

    [Fact]
    public async Task CreateAsync_WhenUnexpectedExceptionOccurs_PropagatesException()
    {
        // Arrange
        var data = CreateData();
        var expected = new InvalidOperationException();
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(expected);

        // Act
        var action = () => CreateAsync(data);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenUnrelatedForeignKeyFails_PropagatesDatabaseException()
    {
        // Arrange
        var data = CreateData();
        var expected = CreateForeignKeyException(
            "fk_unrelated_constraint",
            directException: false);
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(expected);

        // Act
        var action = () => CreateAsync(data);

        // Assert
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        Assert.Same(
            expected,
            exception);
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenWishExists_ReturnsMappedWish()
    {
        // Arrange
        var data = CreateData();
        var wish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(wish);

        // Act
        var result = await _wishService.GetAsync(
            data.WishlistId,
            data.Id,
            data.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            data.Id,
            result.Id);
        VerifyRetrieval(data);
    }

    [Fact]
    public async Task GetAsync_WhenWishDoesNotExist_ReturnsNull()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync((Wish?)null);

        // Act
        var result = await _wishService.GetAsync(
            data.WishlistId,
            data.Id,
            data.CancellationToken);

        // Assert
        Assert.Null(result);
        VerifyRetrieval(data);
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishService.GetAsync(
            data.WishlistId,
            data.Id,
            data.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyRetrieval(data);
    }

    [Fact]
    public async Task UpdateAsync_WhenValuesChange_SavesAndReturnsUpdatedWish()
    {
        // Arrange
        var data = CreateData();
        var wish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(wish);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await UpdateAsync(
            data,
            "Nouvelle console",
            null,
            "https://example.com/new-console",
            399.99m);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "Nouvelle console",
            result.Name);
        Assert.Null(result.Note);
        Assert.Equal(
            "https://example.com/new-console",
            result.Url);
        Assert.Equal(
            399.99m,
            result.Price);
        Assert.Equal(
            1,
            result.Position);
        VerifyTrackedRetrieval(data);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenValuesAreUnchanged_ReturnsWithoutSaving()
    {
        // Arrange
        var data = CreateData();
        var wish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(wish);

        // Act
        var result = await UpdateAsync(
            data,
            data.Name,
            data.Note,
            data.Url,
            data.Price);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            data.Name,
            result.Name);
        VerifyTrackedRetrieval(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenVersionIsStale_ThrowsWishVersionConflictException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(CreateWish(data));

        // Act
        var action = () => UpdateAsync(
            data,
            data.Name,
            data.Note,
            data.Url,
            data.Price,
            expectedVersion: 1);

        // Assert
        await Assert.ThrowsAsync<WishVersionConflictException>(action);
        VerifyTrackedRetrieval(data);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.Owner, null)]
    [InlineData(WishlistAccess.NotOwned, typeof(WishlistNotFoundException))]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    public async Task UpdateAsync_WhenWishIsMissing_ResolvesParentAccess(
        WishlistAccess access,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        ConfigureMissingTrackedWish(
            data,
            access);

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.Null(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyTrackedRetrievalAndAccess(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenMissingWishAccessCheckTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync((Wish?)null);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        VerifyTrackedRetrievalAndAccess(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenTrackedLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyTrackedRetrieval(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenUnexpectedExceptionOccurs_PropagatesException()
    {
        // Arrange
        var data = CreateData();
        var expected = new InvalidOperationException();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(expected);

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        VerifyTrackedRetrieval(data);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.NotOwned, false, typeof(WishlistNotFoundException))]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(WishVersionConflictException))]
    public async Task UpdateAsync_WhenConcurrencyFails_ResolvesCurrentResource(
        WishlistAccess access,
        bool wishStillExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        ConfigureConcurrencyFailure(
            data,
            access,
            wishStillExists);

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.Null(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyConcurrencyFailure(
            data,
            access is WishlistAccess.Owner);
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        var wish = ConfigureFailedSave(
            data,
            new DbUpdateConcurrencyException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyFailedSave(
            data,
            wish);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenCommitAcknowledgementIsLostAndWishMatches_ReturnsCommittedWish()
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureFailedSave(
            data,
            new TimeoutException());
        var committedWish = new Wish(
            data.Id,
            data.WishlistId,
            "Nouvelle console",
            null,
            null,
            null,
            1);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(committedWish);

        // Act
        var result = await UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "Nouvelle console",
            result.Name);
        VerifyFailedSave(
            data,
            attemptedWish);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenCommitAcknowledgementIsLostAndOriginalRemains_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureFailedSave(
            data,
            new TimeoutException());
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(CreateWish(data));

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyFailedSave(
            data,
            attemptedWish);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.NotOwned, false, typeof(WishlistNotFoundException))]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(WishVersionConflictException))]
    public async Task UpdateAsync_WhenAmbiguousUpdateDiffers_ResolvesCurrentResource(
        WishlistAccess access,
        bool wishStillExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureFailedSave(
            data,
            new TimeoutException());
        var currentWish = wishStillExists
            ? new Wish(
                data.Id,
                data.WishlistId,
                "Concurrent console",
                data.Note,
                data.Url,
                data.Price,
                1)
            : null;
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(currentWish);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.Null(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyFailedSave(
            data,
            attemptedWish);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenAmbiguousLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureFailedSave(
            data,
            new TimeoutException());
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyFailedSave(
            data,
            attemptedWish);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenAmbiguousAccessCheckTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        var attemptedWish = ConfigureFailedSave(
            data,
            new TimeoutException());
        var currentWish = new Wish(
            data.Id,
            data.WishlistId,
            "Concurrent console",
            data.Note,
            data.Url,
            data.Price,
            1);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(currentWish);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyFailedSave(
            data,
            attemptedWish);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenSaveThrowsUnexpectedException_PropagatesException()
    {
        // Arrange
        var data = CreateData();
        var expected = new InvalidOperationException();
        var attemptedWish = ConfigureFailedSave(
            data,
            expected);

        // Act
        var action = () => UpdateAsync(
            data,
            "Nouvelle console",
            null,
            null,
            null);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        VerifyFailedSave(
            data,
            attemptedWish);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenSaveSucceeds_ReturnsTrue()
    {
        // Arrange
        var data = CreateData();
        var wish = ConfigureDeletion(
            data,
            1);

        // Act
        var result = await DeleteAsync(data);

        // Assert
        Assert.True(result);
        VerifyDeletion(
            data,
            wish);
    }

    [Fact]
    public async Task DeleteAsync_WhenVersionIsStale_ThrowsWishVersionConflictException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(CreateWish(data));

        // Act
        var action = () => DeleteAsync(
            data,
            expectedVersion: 1);

        // Assert
        await Assert.ThrowsAsync<WishVersionConflictException>(action);
        VerifyTrackedRetrieval(data);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.Owner, null)]
    [InlineData(WishlistAccess.NotOwned, typeof(WishlistNotFoundException))]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    public async Task DeleteAsync_WhenWishIsMissing_ResolvesParentAccess(
        WishlistAccess access,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        ConfigureMissingTrackedWish(
            data,
            access);

        // Act
        var action = () => DeleteAsync(data);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.False(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyTrackedRetrievalAndAccess(data);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenTrackedLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var data = CreateData();
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => DeleteAsync(data);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyTrackedRetrieval(data);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(WishVersionConflictException))]
    [InlineData(WishlistAccess.NotOwned, false, typeof(WishlistNotFoundException))]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    public async Task DeleteAsync_WhenConcurrencyFails_ResolvesCurrentResource(
        WishlistAccess access,
        bool wishStillExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        var wish = ConfigureFailedDeletion(
            data,
            new DbUpdateConcurrencyException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);

        if (access is WishlistAccess.Owner)
        {
            _wishRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    data.WishlistId,
                    data.Id,
                    data.CancellationToken))
                .ReturnsAsync(wishStillExists
                    ? CreateWish(data)
                    : null);
        }

        // Act
        var action = () => DeleteAsync(data);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.False(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyFailedDeletion(
            data,
            wish,
            verifiesCurrentWish: access is WishlistAccess.Owner);
    }

    [Theory]
    [InlineData(WishlistAccess.NotOwned, false, null)]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(DependencyUnavailableException))]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    public async Task DeleteAsync_WhenCommitAcknowledgementIsLost_ResolvesCurrentResource(
        WishlistAccess access,
        bool wishStillExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var data = CreateData();
        var wish = ConfigureFailedDeletion(
            data,
            new TimeoutException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);

        if (access is WishlistAccess.Owner)
        {
            _wishRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    data.WishlistId,
                    data.Id,
                    data.CancellationToken))
                .ReturnsAsync(wishStillExists
                    ? CreateWish(data)
                    : null);
        }

        // Act
        var action = () => DeleteAsync(data);

        // Assert
        if (expectedExceptionType is null)
        {
            var result = await action();
            Assert.True(result);
        }
        else
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(action);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyFailedDeletion(
            data,
            wish,
            verifiesCurrentWish: access is WishlistAccess.Owner);
    }

    [Fact]
    public async Task DeleteAsync_WhenSaveThrowsUnexpectedException_PropagatesException()
    {
        // Arrange
        var data = CreateData();
        var expected = new InvalidOperationException();
        var wish = ConfigureFailedDeletion(
            data,
            expected);

        // Act
        var action = () => DeleteAsync(data);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        VerifyFailedDeletion(
            data,
            wish,
            verifiesCurrentWish: false,
            verifiesAccess: false);
    }

    private Task<WishDetails?> CreateAsync(WishServiceTestData data)
    {
        return _wishService.CreateAsync(
            data.Id,
            data.OwnerId,
            data.WishlistId,
            data.Name,
            data.Note,
            data.Url,
            data.Price,
            data.CancellationToken);
    }

    private Task<WishDetails?> UpdateAsync(
        WishServiceTestData data,
        string name,
        string? note,
        string? url,
        decimal? price,
        uint expectedVersion = 0)
    {
        return _wishService.UpdateAsync(
            data.OwnerId,
            data.WishlistId,
            data.Id,
            name,
            note,
            url,
            price,
            expectedVersion,
            data.CancellationToken);
    }

    private Task<bool> DeleteAsync(
        WishServiceTestData data,
        uint expectedVersion = 0)
    {
        return _wishService.DeleteAsync(
            data.OwnerId,
            data.WishlistId,
            data.Id,
            expectedVersion,
            data.CancellationToken);
    }

    private Wish ConfigureDeletion(
        WishServiceTestData data,
        int savedChanges)
    {
        var wish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(wish);
        _wishRepositoryMock
            .Setup(repository => repository.Remove(wish));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ReturnsAsync(savedChanges);

        return wish;
    }

    private Wish ConfigureFailedDeletion(
        WishServiceTestData data,
        Exception exception)
    {
        var wish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(wish);
        _wishRepositoryMock
            .Setup(repository => repository.Remove(wish));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ThrowsAsync(exception);

        return wish;
    }

    private void VerifyDeletion(
        WishServiceTestData data,
        Wish wish)
    {
        VerifyTrackedRetrieval(data);
        _wishRepositoryMock.Verify(
            repository => repository.Remove(wish),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyFailedDeletion(
        WishServiceTestData data,
        Wish wish,
        bool verifiesCurrentWish,
        bool verifiesAccess = true)
    {
        VerifyTrackedRetrieval(data);
        _wishRepositoryMock.Verify(
            repository => repository.Remove(wish),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);

        if (verifiesAccess)
        {
            _wishlistRepositoryMock.Verify(
                repository => repository.GetAccessAsync(
                    data.OwnerId,
                    data.WishlistId,
                    data.CancellationToken),
                Times.Once);
        }

        if (verifiesCurrentWish)
        {
            _wishRepositoryMock.Verify(
                repository => repository.GetByIdAsync(
                    data.WishlistId,
                    data.Id,
                    data.CancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void ConfigureMissingTrackedWish(
        WishServiceTestData data,
        WishlistAccess access)
    {
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync((Wish?)null);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);
    }

    private Wish ConfigureFailedSave(
        WishServiceTestData data,
        Exception exception)
    {
        var wish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken))
            .ReturnsAsync(wish);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ThrowsAsync(exception);

        return wish;
    }

    private void ConfigureConcurrencyFailure(
        WishServiceTestData data,
        WishlistAccess access,
        bool wishStillExists)
    {
        _ = ConfigureFailedSave(
            data,
            new DbUpdateConcurrencyException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(access);

        if (access is WishlistAccess.Owner)
        {
            _wishRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    data.WishlistId,
                    data.Id,
                    data.CancellationToken))
                .ReturnsAsync(wishStillExists
                    ? CreateWish(data)
                    : null);
        }
    }

    private void VerifyConcurrencyFailure(
        WishServiceTestData data,
        bool verifiesWish)
    {
        VerifyTrackedRetrieval(data);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);

        if (verifiesWish)
        {
            _wishRepositoryMock.Verify(
                repository => repository.GetByIdAsync(
                    data.WishlistId,
                    data.Id,
                    data.CancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyFailedSave(
        WishServiceTestData data,
        Wish wish)
    {
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        Assert.Equal(
            "Nouvelle console",
            wish.Name);
    }

    private void VerifyTrackedRetrieval(WishServiceTestData data)
    {
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
    }

    private void VerifyTrackedRetrievalAndAccess(WishServiceTestData data)
    {
        VerifyTrackedRetrieval(data);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
    }

    private Wish ConfigureAmbiguousCreation(WishServiceTestData data)
    {
        var attemptedWish = CreateWish(data);
        _wishRepositoryMock
            .Setup(repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(1);
        _wishRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wish>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        return attemptedWish;
    }

    private void VerifyCreation(
        WishServiceTestData data,
        Wish wish)
    {
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.Add(wish),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyAmbiguousCreation(
        WishServiceTestData data,
        Wish attemptedWish,
        bool verifiesAccess)
    {
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.Add(It.Is<Wish>(wish =>
                wish.Id == attemptedWish.Id && wish.WishlistId == attemptedWish.WishlistId)),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);

        if (verifiesAccess)
        {
            _wishlistRepositoryMock.Verify(
                repository => repository.GetAccessAsync(
                    data.OwnerId,
                    data.WishlistId,
                    data.CancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyAllocationAndAccess(WishServiceTestData data)
    {
        _wishRepositoryMock.Verify(
            repository => repository.AllocatePositionAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyRetrieval(WishServiceTestData data)
    {
        _wishRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                data.WishlistId,
                data.Id,
                data.CancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private static Mock<IWishTransaction> CreateTransactionMock()
    {
        var transactionMock = new Mock<IWishTransaction>(MockBehavior.Strict);
        transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        return transactionMock;
    }

    private static WishPositionSequence CreateSequence(Guid wishlistId)
    {
        return new WishPositionSequence(
            wishlistId,
            4,
            3,
            42);
    }

    private void SetupOwnedAccess(WishServiceTestData data)
    {
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
    }

    private void VerifyOwnedAccess(WishServiceTestData data)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                data.OwnerId,
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
    }

    private void SetupReorder(
        WishServiceTestData data,
        Mock<IWishTransaction> transactionMock,
        WishPositionSequence sequence,
        IReadOnlyCollection<Wish> wishes)
    {
        SetupOwnedAccess(data);
        _wishTransactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken))
            .ReturnsAsync(transactionMock.Object);
        _wishRepositoryMock
            .Setup(repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(sequence);
        _wishRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken))
            .ReturnsAsync(wishes);
    }

    private void VerifyReorderSetup(
        WishServiceTestData data,
        Mock<IWishTransaction> transactionMock)
    {
        VerifyOwnedAccess(data);
        _wishTransactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetCollectionStateForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        _wishRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                data.WishlistId,
                data.CancellationToken),
            Times.Once);
        transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    private void VerifyNoOtherCalls()
    {
        _wishRepositoryMock.VerifyNoOtherCalls();
        _wishlistRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
        _wishTransactionFactoryMock.VerifyNoOtherCalls();
    }

    private static WishServiceTestData CreateData()
    {
        return new WishServiceTestData(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Console",
            "Édition blanche",
            "https://example.com/console",
            499.99m,
            TestContext.Current.CancellationToken);
    }

    private static Wish CreateWish(WishServiceTestData data)
    {
        return new Wish(
            data.Id,
            data.WishlistId,
            data.Name,
            data.Note,
            data.Url,
            data.Price,
            1);
    }

    private static Wish CreateWish(
        Guid id,
        Guid wishlistId,
        string name,
        long position)
    {
        return new Wish(
            id,
            wishlistId,
            name,
            null,
            null,
            null,
            position);
    }

    private static Wish[] CreatePersistedOrder(
        Guid wishlistId,
        IReadOnlyCollection<Guid> wishIds)
    {
        return wishIds
            .Select((
                wishId,
                index) => CreateWish(
                    wishId,
                    wishlistId,
                    $"Persisted {index}",
                    index + 1))
            .ToArray();
    }

    private static Exception CreateForeignKeyException(
        string constraintName,
        bool directException)
    {
        var postgresException = new PostgresException(
            "PostgreSQL constraint violation.",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.ForeignKeyViolation,
            constraintName: constraintName);

        return directException
            ? postgresException
            : new DbUpdateException(
                "PostgreSQL constraint violation.",
                postgresException);
    }

    private static Exception CreateWishLimitException(bool directException)
    {
        var postgresException = new PostgresException(
            "Wishlist wish limit reached.",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.CheckViolation,
            constraintName: "ck_wish_position_sequences_current_count_limit");

        return directException
            ? postgresException
            : new DbUpdateException(
                "Wishlist wish limit reached.",
                postgresException);
    }

}
