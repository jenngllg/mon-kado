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
            _unitOfWorkMock.Object);
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

    private void VerifyNoOtherCalls()
    {
        _wishlistRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }
}
