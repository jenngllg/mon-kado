using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistServiceTests
{
    private const string OwnerForeignKeyName = "fk_wishlists_users_owner_id";
    private const string OwnerNormalizedNameIndexName = "ux_wishlists_owner_normalized_name";

    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IWishlistRepository> _wishlistRepositoryMock;
    private readonly WishlistService _wishlistService;

    public WishlistServiceTests()
    {
        _wishlistRepositoryMock = new Mock<IWishlistRepository>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _wishlistService = new WishlistService(
            _wishlistRepositoryMock.Object,
            _unitOfWorkMock.Object,
            new FixedTimeProvider(new DateTimeOffset(
                2026,
                8,
                25,
                10,
                0,
                0,
                TimeSpan.Zero)));
    }

    [Fact]
    public async Task CreateAsync_WhenSaveSucceeds_ReturnsCreatedWishlist()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var eventDate = new DateOnly(
            2099,
            9,
            24);
        Wishlist? addedWishlist = null;
        _wishlistRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wishlist>()))
            .Callback<Wishlist>(wishlist => addedWishlist = wishlist);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _wishlistService.CreateAsync(
            id,
            ownerId,
            "Liste",
            "LISTE",
            WishlistOccasion.Birthday,
            eventDate,
            "Merci",
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(addedWishlist);
        Assert.Equal(
            id,
            result.Id);
        Assert.Equal(
            "Liste",
            result.Name);
        Assert.Equal(
            ownerId,
            addedWishlist.OwnerId);
        Assert.Equal(
            "LISTE",
            addedWishlist.NormalizedName);
        Assert.Equal(
            WishlistOccasion.Birthday,
            result.Occasion);
        Assert.Equal(
            eventDate,
            result.EventDate);
        Assert.Equal(
            "Merci",
            result.Message);
        _wishlistRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wishlist>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenNameAlreadyExists_ThrowsWishlistNameAlreadyExistsException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wishlist>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(CreatePostgreSqlException(
                PostgresErrorCodes.UniqueViolation,
                OwnerNormalizedNameIndexName));

        // Act
        var action = () => CreateAsync(cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNameAlreadyExistsException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wishlist>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenOwnerDoesNotExist_ReturnsNull()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wishlist>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(CreatePostgreSqlException(
                PostgresErrorCodes.ForeignKeyViolation,
                OwnerForeignKeyName));

        // Act
        var result = await CreateAsync(cancellationToken);

        // Assert
        Assert.Null(result);
        _wishlistRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wishlist>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wishlist>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => CreateAsync(cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        _wishlistRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wishlist>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenSaveThrowsUnexpectedException_PropagatesException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new InvalidOperationException();
        _wishlistRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wishlist>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var action = () => CreateAsync(cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expected,
            exception);
        _wishlistRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wishlist>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "another_foreign_key")]
    [InlineData(PostgresErrorCodes.UniqueViolation, "another_unique_index")]
    public async Task CreateAsync_WhenSaveViolatesUnrelatedConstraint_PropagatesException(
        string sqlState,
        string constraintName)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = CreatePostgreSqlException(
            sqlState,
            constraintName);
        _wishlistRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<Wishlist>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var action = () => CreateAsync(cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        Assert.Same(
            expected,
            exception);
        _wishlistRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<Wishlist>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenWishlistExists_ReturnsWishlist()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var wishlist = CreateWishlist(wishlistId);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);

        // Act
        var result = await _wishlistService.GetAsync(
            wishlistId,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            wishlistId,
            result.Id);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenWishlistDoesNotExist_ReturnsNull()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync((Wishlist?)null);

        // Act
        var result = await _wishlistService.GetAsync(
            wishlistId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishlistService.GetAsync(
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenValuesChange_ReturnsUpdatedWishlistAndSaves()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var eventDate = new DateOnly(
            2026,
            9,
            24);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Nouvelle liste",
            "NOUVELLE LISTE",
            WishlistOccasion.Wedding,
            eventDate,
            "Merci",
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "Nouvelle liste",
            result.Name);
        Assert.Equal(
            WishlistOccasion.Wedding,
            result.Occasion);
        Assert.Equal(
            eventDate,
            result.EventDate);
        Assert.Equal(
            "Merci",
            result.Message);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenValuesAreUnchanged_ReturnsWishlistWithoutSaving()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);

        // Act
        var result = await _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            null,
            null,
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            0u,
            result.Version);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenExistingPastDateIsUnchanged_ReturnsWishlistWithoutSaving()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var pastDate = new DateOnly(
            2020,
            1,
            1);
        var wishlist = new Wishlist(
            wishlistId,
            ownerId,
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            pastDate,
            null);
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);

        // Act
        var result = await _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            pastDate,
            null,
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            pastDate,
            result.EventDate);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenPastDateIsChangedToAnotherPastDate_ThrowsRequestValidationException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = new Wishlist(
            wishlistId,
            ownerId,
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            new DateOnly(
                2020,
                1,
                1),
            null);
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);

        // Act
        var action = () => _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            new DateOnly(
                2021,
                1,
                1),
            null,
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<RequestValidationException>(action);
        var error = Assert.Single(exception.ValidationErrors);
        Assert.Equal(
            "eventDate",
            error.PropertyName);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenWishlistDoesNotExist_ReturnsNull()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync((Wishlist?)null);

        // Act
        var result = await UpdateAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenExpectedVersionIsStale_ThrowsWishlistVersionConflictException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);

        // Act
        var action = () => _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            null,
            null,
            42,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistVersionConflictException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenNameAlreadyExists_ThrowsWishlistNameAlreadyExistsException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateWishlist(wishlistId));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(CreatePostgreSqlException(
                PostgresErrorCodes.UniqueViolation,
                OwnerNormalizedNameIndexName));

        // Act
        var action = () => _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Doublon",
            "DOUBLON",
            WishlistOccasion.Other,
            null,
            null,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistNameAlreadyExistsException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.Owner, typeof(WishlistVersionConflictException))]
    public async Task UpdateAsync_WhenConcurrentUpdateStillHasAccess_ThrowsExpectedException(
        WishlistAccess access,
        Type expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateWishlist(wishlistId));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(access);

        // Act
        var action = () => _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Nouvelle liste",
            "NOUVELLE LISTE",
            WishlistOccasion.Other,
            null,
            null,
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        Assert.IsType(
            expectedExceptionType,
            exception);
        VerifyConcurrentUpdate(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task UpdateAsync_WhenWishlistIsDeletedConcurrently_ReturnsNull()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateWishlist(wishlistId));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        var result = await _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Nouvelle liste",
            "NOUVELLE LISTE",
            WishlistOccasion.Other,
            null,
            null,
            0,
            cancellationToken);

        // Assert
        Assert.Null(result);
        VerifyConcurrentUpdate(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrentAccessCheckTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateWishlist(wishlistId));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyConcurrentUpdate(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task UpdateAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => UpdateAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenWishlistExists_RemovesWishlistAndSaves()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _wishlistRepositoryMock
            .Setup(repository => repository.Remove(wishlist));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.True(result);
        VerifyDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenWishlistDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync((Wishlist?)null);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        var result = await _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.False(result);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenMemberDisappearsBeforeLoadingWishlist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync((Wishlist?)null);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.MemberNotFound);

        // Act
        var action = () => _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenExpectedVersionIsStale_ThrowsWishlistVersionConflictException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateWishlist(wishlistId));

        // Act
        var action = () => _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            42,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistVersionConflictException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.Owner, typeof(WishlistVersionConflictException))]
    public async Task DeleteAsync_WhenConcurrentDeletionStillHasAccess_ThrowsExpectedException(
        WishlistAccess access,
        Type expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        ConfigureConcurrentDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(access);

        // Act
        var action = () => _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        Assert.IsType(
            expectedExceptionType,
            exception);
        VerifyConcurrentDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenWishlistIsDeletedConcurrently_ReturnsFalse()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        ConfigureConcurrentDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        var result = await _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.False(result);
        VerifyConcurrentDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenConcurrentAccessCheckTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishlist = CreateWishlist(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        ConfigureConcurrentDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyConcurrentDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishlistService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByOwnerIdAsync_WhenMemberExists_ReturnsMappedWishlistsInRepositoryOrder()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var firstWishlist = CreateWishlist(Guid.CreateVersion7());
        var secondWishlist = CreateWishlist(Guid.CreateVersion7());
        var cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyCollection<Wishlist> wishlists =
        [
            firstWishlist,
            secondWishlist
        ];
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken))
            .ReturnsAsync(wishlists);

        // Act
        var result = await _wishlistService.GetByOwnerIdAsync(
            ownerId,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            [
                firstWishlist.Id,
                secondWishlist.Id
            ],
            result.Select(wishlist => wishlist.Id));
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByOwnerIdAsync_WhenMemberDoesNotExist_ReturnsNull()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken))
            .ReturnsAsync((IReadOnlyCollection<Wishlist>?)null);

        // Act
        var result = await _wishlistService.GetByOwnerIdAsync(
            ownerId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByOwnerIdAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishlistService.GetByOwnerIdAsync(
            ownerId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound)]
    [InlineData(WishlistAccess.NotOwned)]
    [InlineData(WishlistAccess.Owner)]
    public async Task GetAccessAsync_WhenRepositoryReturnsAccess_ReturnsAccess(WishlistAccess expected)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _wishlistService.GetAccessAsync(
            memberId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.Equal(
            expected,
            result);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAccessAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _wishlistService.GetAccessAsync(
            memberId,
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private Task<WishlistDetails?> CreateAsync(CancellationToken cancellationToken)
    {
        return _wishlistService.CreateAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            null,
            null,
            cancellationToken);
    }

    private Task<WishlistDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return _wishlistService.UpdateAsync(
            ownerId,
            wishlistId,
            "Nouvelle liste",
            "NOUVELLE LISTE",
            WishlistOccasion.Other,
            null,
            null,
            0,
            cancellationToken);
    }

    private static Wishlist CreateWishlist(Guid wishlistId)
    {
        return new Wishlist(
            wishlistId,
            Guid.CreateVersion7(),
            "Liste",
            "LISTE",
            WishlistOccasion.Other,
            null,
            null);
    }

    private static DbUpdateException CreatePostgreSqlException(
        string sqlState,
        string constraintName)
    {
        return new DbUpdateException(
            "PostgreSQL constraint violation.",
            new PostgresException(
                "PostgreSQL constraint violation.",
                "ERROR",
                "ERROR",
                sqlState,
                constraintName: constraintName));
    }

    private void VerifyConcurrentUpdate(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void ConfigureConcurrentDeletion(
        Guid ownerId,
        Guid wishlistId,
        Wishlist wishlist,
        CancellationToken cancellationToken)
    {
        _wishlistRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);
        _wishlistRepositoryMock
            .Setup(repository => repository.Remove(wishlist));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());
    }

    private void VerifyDeletion(
        Guid ownerId,
        Guid wishlistId,
        Wishlist wishlist,
        CancellationToken cancellationToken)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistRepositoryMock.Verify(
            repository => repository.Remove(wishlist),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyConcurrentDeletion(
        Guid ownerId,
        Guid wishlistId,
        Wishlist wishlist,
        CancellationToken cancellationToken)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyDeletion(
            ownerId,
            wishlistId,
            wishlist,
            cancellationToken);
    }

    private void VerifyNoOtherCalls()
    {
        _wishlistRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }
}
