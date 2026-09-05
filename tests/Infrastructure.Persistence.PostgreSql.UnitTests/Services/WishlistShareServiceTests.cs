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

public class WishlistShareServiceTests
{
    private const string WishlistForeignKeyName = "fk_wishlist_share_links_wishlists_wishlist_id";
    private const string WishlistIndexName = "ux_wishlist_share_links_wishlist_id";

    private readonly Mock<IWishlistShareLinkRepository> _shareLinkRepositoryMock;
    private readonly Mock<IWishlistRepository> _wishlistRepositoryMock;
    private readonly Mock<IWishlistShareTokenService> _tokenServiceMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly WishlistShareService _shareService;

    public WishlistShareServiceTests()
    {
        _shareLinkRepositoryMock = new Mock<IWishlistShareLinkRepository>(MockBehavior.Strict);
        _wishlistRepositoryMock = new Mock<IWishlistRepository>(MockBehavior.Strict);
        _tokenServiceMock = new Mock<IWishlistShareTokenService>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _shareService = new WishlistShareService(
            _shareLinkRepositoryMock.Object,
            _wishlistRepositoryMock.Object,
            _tokenServiceMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WhenSaveSucceeds_ReturnsCreatedShareLink()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = CreateToken();
        WishlistShareLink? addedShareLink = null;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _tokenServiceMock
            .Setup(service => service.Create())
            .Returns(token);
        _shareLinkRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<WishlistShareLink>()))
            .Callback<WishlistShareLink>(shareLink => addedShareLink = shareLink);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _shareService.CreateAsync(
            id,
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(addedShareLink);
        Assert.Equal(
            id,
            result.Id);
        Assert.Equal(
            wishlistId,
            result.WishlistId);
        Assert.Equal(
            token.Secret,
            result.Secret);
        Assert.Equal(
            token.SecretHash,
            addedShareLink.SecretHash);
        Assert.NotSame(
            token.SecretHash,
            addedShareLink.SecretHash);
        Assert.Equal(
            token.ProtectedSecret,
            addedShareLink.ProtectedSecret);
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _tokenServiceMock.Verify(
            service => service.Create(),
            Times.Once);
        _shareLinkRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<WishlistShareLink>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.NotOwned, typeof(WishlistNotFoundException))]
    public async Task CreateAsync_WhenWishlistAccessIsDenied_ThrowsExpectedException(
        WishlistAccess access,
        Type expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(access);

        // Act
        var exception = await Record.ExceptionAsync(() => _shareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            cancellationToken));

        // Assert
        Assert.NotNull(exception);
        Assert.IsType(
            expectedExceptionType,
            exception);
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenOwnershipLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _shareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistAlreadyHasShareLink_ThrowsWishlistShareLinkAlreadyExistsException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupCreate(
            ownerId,
            wishlistId,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(CreatePostgreSqlException(
                PostgresErrorCodes.UniqueViolation,
                WishlistIndexName));

        // Act
        var action = () => _shareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkAlreadyExistsException>(action);
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistDisappearsDuringSave_ReturnsNull()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupCreate(
            ownerId,
            wishlistId,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(CreatePostgreSqlException(
                PostgresErrorCodes.ForeignKeyViolation,
                WishlistForeignKeyName));

        // Act
        var result = await _shareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task CreateAsync_WhenUnrelatedConstraintFails_PropagatesDbUpdateException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = CreatePostgreSqlException(
            PostgresErrorCodes.UniqueViolation,
            "another_constraint");
        SetupCreate(
            ownerId,
            wishlistId,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var exception = await Record.ExceptionAsync(() => _shareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            cancellationToken));

        // Assert
        Assert.Same(
            expected,
            exception);
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task CreateAsync_WhenCommitAcknowledgementIsLost_ReturnsPersistedShareLink()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = CreateToken();
        WishlistShareLink? attemptedShareLink = null;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _tokenServiceMock
            .Setup(service => service.Create())
            .Returns(token);
        _shareLinkRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<WishlistShareLink>()))
            .Callback<WishlistShareLink>(shareLink => attemptedShareLink = shareLink);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                id,
                cancellationToken))
            .ReturnsAsync(() => attemptedShareLink);

        // Act
        var result = await _shareService.CreateAsync(
            id,
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            token.Secret,
            result.Secret);
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesReconciliation: true);
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.Owner, typeof(DependencyUnavailableException))]
    public async Task CreateAsync_WhenAmbiguousCreationIsNotPersisted_ThrowsExpectedException(
        WishlistAccess reconciliationAccess,
        Type expectedExceptionType)
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupCreate(
            ownerId,
            wishlistId,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                id,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);
        _wishlistRepositoryMock
            .SetupSequence(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner)
            .ReturnsAsync(reconciliationAccess);

        // Act
        var exception = await Record.ExceptionAsync(() => _shareService.CreateAsync(
            id,
            ownerId,
            wishlistId,
            cancellationToken));

        // Assert
        Assert.NotNull(exception);
        Assert.IsType(
            expectedExceptionType,
            exception);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Exactly(2));
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesOwner: false,
            verifiesReconciliation: true);
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistDisappearsAfterAmbiguousCreation_ReturnsNull()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupCreate(
            ownerId,
            wishlistId,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                id,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);
        _wishlistRepositoryMock
            .SetupSequence(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner)
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        var result = await _shareService.CreateAsync(
            id,
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Exactly(2));
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesOwner: false,
            verifiesReconciliation: true);
    }

    [Fact]
    public async Task CreateAsync_WhenAmbiguousCreationReadTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupCreate(
            ownerId,
            wishlistId,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                id,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _shareService.CreateAsync(
            id,
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyCreate(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesReconciliation: true);
    }

    [Fact]
    public async Task GetAsync_WhenShareLinkExists_ReturnsUnprotectedSecret()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Unprotect(shareLink.ProtectedSecret))
            .Returns("owner-secret");

        // Act
        var result = await _shareService.GetAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "owner-secret",
            result.Secret);
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _tokenServiceMock.Verify(
            service => service.Unprotect(shareLink.ProtectedSecret),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenShareLinkDoesNotExist_ReturnsNull()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);

        // Act
        var result = await _shareService.GetAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenShareLinkLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _shareService.GetAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSharedAsync_WhenSecretIsValid_ReturnsSharedWishlist()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var shareLink = CreateShareLink(wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = CreateSharedWishlist(wishlistId);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Verify(
                "valid-secret",
                shareLink.SecretHash))
            .Returns(true);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetSharedWishlistAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _shareService.GetSharedAsync(
            shareLink.Id,
            "valid-secret",
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        VerifySharedLookup(
            shareLink,
            "valid-secret",
            cancellationToken,
            verifiesContent: true);
    }

    [Fact]
    public async Task GetSharedAsync_WhenShareLinkDoesNotExist_ReturnsNullWithoutVerifyingSecret()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLinkId,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);

        // Act
        var result = await _shareService.GetSharedAsync(
            shareLinkId,
            "secret",
            cancellationToken);

        // Assert
        Assert.Null(result);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                shareLinkId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSharedAsync_WhenSecretIsInvalid_ReturnsNullWithoutLoadingContent()
    {
        // Arrange
        var shareLink = CreateShareLink(Guid.CreateVersion7());
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Verify(
                "invalid-secret",
                shareLink.SecretHash))
            .Returns(false);

        // Act
        var result = await _shareService.GetSharedAsync(
            shareLink.Id,
            "invalid-secret",
            cancellationToken);

        // Assert
        Assert.Null(result);
        VerifySharedLookup(
            shareLink,
            "invalid-secret",
            cancellationToken,
            verifiesContent: false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetSharedAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException(
        bool failsDuringLinkLookup)
    {
        // Arrange
        var shareLink = CreateShareLink(Guid.CreateVersion7());
        var cancellationToken = TestContext.Current.CancellationToken;

        if (failsDuringLinkLookup)
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    shareLink.Id,
                    cancellationToken))
                .ThrowsAsync(new TimeoutException());
        }
        else
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    shareLink.Id,
                    cancellationToken))
                .ReturnsAsync(shareLink);
            _tokenServiceMock
                .Setup(service => service.Verify(
                    "secret",
                    shareLink.SecretHash))
                .Returns(true);
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetSharedWishlistAsync(
                    shareLink.WishlistId,
                    cancellationToken))
                .ThrowsAsync(new TimeoutException());
        }

        // Act
        var action = () => _shareService.GetSharedAsync(
            shareLink.Id,
            "secret",
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken),
            Times.Once);

        if (!failsDuringLinkLookup)
        {
            _tokenServiceMock.Verify(
                service => service.Verify(
                    "secret",
                    shareLink.SecretHash),
                Times.Once);
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetSharedWishlistAsync(
                    shareLink.WishlistId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSharedWishAsync_WhenWishExists_ReturnsFoundResult()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var shareLink = CreateShareLink(wishlistId);
        var wish = CreateSharedWish();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Verify(
                "valid-secret",
                shareLink.SecretHash))
            .Returns(true);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetSharedWishAsync(
                wishlistId,
                wish.Id,
                cancellationToken))
            .ReturnsAsync(wish);

        // Act
        var result = await _shareService.GetSharedWishAsync(
            shareLink.Id,
            "valid-secret",
            wish.Id,
            cancellationToken);

        // Assert
        Assert.Equal(
            SharedWishLookupOutcome.Found,
            result.Outcome);
        Assert.Equal(
            wishlistId,
            result.WishlistId);
        Assert.Same(
            wish,
            result.Wish);
        VerifySharedWishLookup(
            shareLink,
            "valid-secret",
            wish.Id,
            verifiesContent: true,
            cancellationToken);
    }

    [Fact]
    public async Task GetSharedWishAsync_WhenShareLinkDoesNotExist_ReturnsSharedWishlistNotFound()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLinkId,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);

        // Act
        var result = await _shareService.GetSharedWishAsync(
            shareLinkId,
            "secret",
            wishId,
            cancellationToken);

        // Assert
        Assert.Equal(
            SharedWishLookupOutcome.SharedWishlistNotFound,
            result.Outcome);
        Assert.Null(result.WishlistId);
        Assert.Null(result.Wish);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                shareLinkId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSharedWishAsync_WhenSecretIsInvalid_ReturnsSharedWishlistNotFound()
    {
        // Arrange
        var shareLink = CreateShareLink(Guid.CreateVersion7());
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Verify(
                "invalid-secret",
                shareLink.SecretHash))
            .Returns(false);

        // Act
        var result = await _shareService.GetSharedWishAsync(
            shareLink.Id,
            "invalid-secret",
            wishId,
            cancellationToken);

        // Assert
        Assert.Equal(
            SharedWishLookupOutcome.SharedWishlistNotFound,
            result.Outcome);
        Assert.Null(result.WishlistId);
        Assert.Null(result.Wish);
        VerifySharedWishLookup(
            shareLink,
            "invalid-secret",
            wishId,
            verifiesContent: false,
            cancellationToken);
    }

    [Fact]
    public async Task GetSharedWishAsync_WhenWishDoesNotExist_ReturnsWishNotFound()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var shareLink = CreateShareLink(wishlistId);
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Verify(
                "valid-secret",
                shareLink.SecretHash))
            .Returns(true);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetSharedWishAsync(
                wishlistId,
                wishId,
                cancellationToken))
            .ReturnsAsync((SharedWishDetail?)null);

        // Act
        var result = await _shareService.GetSharedWishAsync(
            shareLink.Id,
            "valid-secret",
            wishId,
            cancellationToken);

        // Assert
        Assert.Equal(
            SharedWishLookupOutcome.WishNotFound,
            result.Outcome);
        Assert.Equal(
            wishlistId,
            result.WishlistId);
        Assert.Null(result.Wish);
        VerifySharedWishLookup(
            shareLink,
            "valid-secret",
            wishId,
            verifiesContent: true,
            cancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetSharedWishAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException(
        bool failsDuringLinkLookup)
    {
        // Arrange
        var shareLink = CreateShareLink(Guid.CreateVersion7());
        var wishId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;

        if (failsDuringLinkLookup)
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    shareLink.Id,
                    cancellationToken))
                .ThrowsAsync(new TimeoutException());
        }
        else
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    shareLink.Id,
                    cancellationToken))
                .ReturnsAsync(shareLink);
            _tokenServiceMock
                .Setup(service => service.Verify(
                    "secret",
                    shareLink.SecretHash))
                .Returns(true);
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetSharedWishAsync(
                    shareLink.WishlistId,
                    wishId,
                    cancellationToken))
                .ThrowsAsync(new TimeoutException());
        }

        // Act
        var action = () => _shareService.GetSharedWishAsync(
            shareLink.Id,
            "secret",
            wishId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken),
            Times.Once);

        if (!failsDuringLinkLookup)
        {
            _tokenServiceMock.Verify(
                service => service.Verify(
                    "secret",
                    shareLink.SecretHash),
                Times.Once);
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetSharedWishAsync(
                    shareLink.WishlistId,
                    wishId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RotateAsync_WhenSaveSucceeds_ReturnsRotatedShareLink()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        var token = CreateToken(
            "new-secret",
            [
                4,
                5,
                6
            ],
            "new-protected-secret");
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Create())
            .Returns(token);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _shareService.RotateAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            token.Secret,
            result.Secret);
        Assert.Equal(
            token.SecretHash,
            shareLink.SecretHash);
        Assert.NotSame(
            token.SecretHash,
            shareLink.SecretHash);
        Assert.Equal(
            token.ProtectedSecret,
            shareLink.ProtectedSecret);
        VerifyRotation(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesToken: true,
            verifiesSave: true);
    }

    [Fact]
    public async Task RotateAsync_WhenShareLinkDoesNotExist_ReturnsNull()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);

        // Act
        var result = await _shareService.RotateAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.Null(result);
        VerifyRotation(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task RotateAsync_WhenVersionDoesNotMatch_ThrowsWishlistShareLinkVersionConflictException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateShareLink(wishlistId));

        // Act
        var action = () => _shareService.RotateAsync(
            ownerId,
            wishlistId,
            42,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkVersionConflictException>(action);
        VerifyRotation(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task RotateAsync_WhenTrackedLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _shareService.RotateAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyRotation(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task RotateAsync_WhenTrackedLookupFailsUnexpectedly_PropagatesException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new InvalidOperationException();
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var exception = await Record.ExceptionAsync(() => _shareService.RotateAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken));

        // Assert
        Assert.Same(
            expected,
            exception);
        VerifyRotation(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.NotOwned, false, null)]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(WishlistShareLinkVersionConflictException))]
    public async Task RotateAsync_WhenConcurrencyFailureOccurs_ResolvesCurrentState(
        WishlistAccess reconciliationAccess,
        bool currentLinkExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupAmbiguousRotation(
            ownerId,
            wishlistId,
            shareLink,
            new DbUpdateConcurrencyException(),
            cancellationToken);
        _wishlistRepositoryMock
            .SetupSequence(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner)
            .ReturnsAsync(reconciliationAccess);

        if (reconciliationAccess is WishlistAccess.Owner)
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByWishlistIdAsync(
                    wishlistId,
                    cancellationToken))
                .ReturnsAsync(currentLinkExists
                    ? shareLink
                    : null);
        }

        // Act
        WishlistShareLinkDetails? result = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await _shareService.RotateAsync(
                ownerId,
                wishlistId,
                0,
                cancellationToken);
        });

        // Assert
        Assert.Null(result);

        if (expectedExceptionType is null)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.NotNull(exception);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyAmbiguousRotation(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesCurrentLink: reconciliationAccess is WishlistAccess.Owner);
    }

    [Fact]
    public async Task RotateAsync_WhenCommitAcknowledgementIsLost_ReturnsPersistedRotation()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupAmbiguousRotation(
            ownerId,
            wishlistId,
            shareLink,
            new TimeoutException(),
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(() => shareLink);

        // Act
        var result = await _shareService.RotateAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            "new-secret",
            result.Secret);
        VerifyAmbiguousRotation(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesById: true,
            verifiesReconciliationAccess: false);
    }

    [Fact]
    public async Task RotateAsync_WhenAmbiguousRotationWasNotCommitted_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        var original = CreateShareLink(
            wishlistId,
            shareLink.Id);
        SetupAmbiguousRotation(
            ownerId,
            wishlistId,
            shareLink,
            new TimeoutException(),
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(original);

        // Act
        var action = () => _shareService.RotateAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyAmbiguousRotation(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesById: true,
            verifiesReconciliationAccess: false);
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.NotOwned, true, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(WishlistShareLinkVersionConflictException))]
    public async Task RotateAsync_WhenAmbiguousRotationHasDifferentState_ResolvesCurrentState(
        WishlistAccess reconciliationAccess,
        bool currentLinkExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        var currentShareLink = currentLinkExists
            ? CreateShareLink(
                wishlistId,
                shareLink.Id,
                [
                    8,
                    8,
                    8
                ],
                "different-protected-secret")
            : null;
        SetupAmbiguousRotation(
            ownerId,
            wishlistId,
            shareLink,
            new TimeoutException(),
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken))
            .ReturnsAsync(currentShareLink);
        _wishlistRepositoryMock
            .SetupSequence(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner)
            .ReturnsAsync(reconciliationAccess);

        // Act
        WishlistShareLinkDetails? result = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await _shareService.RotateAsync(
                ownerId,
                wishlistId,
                0,
                cancellationToken);
        });

        // Assert
        Assert.Null(result);

        if (expectedExceptionType is null)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.NotNull(exception);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyAmbiguousRotation(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesById: true);
    }

    [Fact]
    public async Task DeleteAsync_WhenSaveSucceeds_ReturnsTrue()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _shareLinkRepositoryMock
            .Setup(repository => repository.Remove(shareLink));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        // Act
        var result = await _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.True(result);
        VerifyDeletion(
            ownerId,
            wishlistId,
            shareLink,
            cancellationToken,
            verifiesSave: true);
    }

    [Fact]
    public async Task DeleteAsync_WhenShareLinkDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync((WishlistShareLink?)null);

        // Act
        var result = await _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        Assert.False(result);
        VerifyDeletionLookup(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenVersionDoesNotMatch_ThrowsWishlistShareLinkVersionConflictException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateShareLink(wishlistId));

        // Act
        var action = () => _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            42,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkVersionConflictException>(action);
        VerifyDeletionLookup(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenTrackedLookupTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyDeletionLookup(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenTrackedLookupFailsUnexpectedly_PropagatesException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new InvalidOperationException();
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var exception = await Record.ExceptionAsync(() => _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken));

        // Assert
        Assert.Same(
            expected,
            exception);
        VerifyDeletionLookup(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    [Theory]
    [InlineData(WishlistAccess.MemberNotFound, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.NotOwned, false, null)]
    [InlineData(WishlistAccess.Owner, false, null)]
    [InlineData(WishlistAccess.Owner, true, typeof(WishlistShareLinkVersionConflictException))]
    public async Task DeleteAsync_WhenConcurrencyFailureOccurs_ResolvesCurrentState(
        WishlistAccess reconciliationAccess,
        bool currentLinkExists,
        Type? expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            new DbUpdateConcurrencyException(),
            cancellationToken);
        _wishlistRepositoryMock
            .SetupSequence(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner)
            .ReturnsAsync(reconciliationAccess);

        if (reconciliationAccess is WishlistAccess.Owner)
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByWishlistIdAsync(
                    wishlistId,
                    cancellationToken))
                .ReturnsAsync(currentLinkExists
                    ? shareLink
                    : null);
        }

        // Act
        bool? result = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await _shareService.DeleteAsync(
                ownerId,
                wishlistId,
                0,
                cancellationToken);
        });

        // Assert

        if (expectedExceptionType is null)
        {
            Assert.Null(exception);
            Assert.False(result);
        }
        else
        {
            Assert.NotNull(exception);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            cancellationToken,
            verifiesCurrentLink: reconciliationAccess is WishlistAccess.Owner);
    }

    [Theory]
    [InlineData(WishlistAccess.NotOwned, false, true, null)]
    [InlineData(WishlistAccess.Owner, false, true, null)]
    [InlineData(WishlistAccess.MemberNotFound, false, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(WishlistAccess.Owner, true, false, typeof(DependencyUnavailableException))]
    public async Task DeleteAsync_WhenCommitAcknowledgementIsLost_ResolvesCurrentState(
        WishlistAccess reconciliationAccess,
        bool currentLinkExists,
        bool expectedResult,
        Type? expectedExceptionType)
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            new TimeoutException(),
            cancellationToken);
        _wishlistRepositoryMock
            .SetupSequence(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner)
            .ReturnsAsync(reconciliationAccess);

        if (reconciliationAccess is WishlistAccess.Owner)
        {
            _shareLinkRepositoryMock
                .Setup(repository => repository.GetByWishlistIdAsync(
                    wishlistId,
                    cancellationToken))
                .ReturnsAsync(currentLinkExists
                    ? shareLink
                    : null);
        }

        // Act
        bool? result = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await _shareService.DeleteAsync(
                ownerId,
                wishlistId,
                0,
                cancellationToken);
        });

        // Assert

        if (expectedExceptionType is null)
        {
            Assert.Null(exception);
            Assert.Equal(
                expectedResult,
                result);
        }
        else
        {
            Assert.NotNull(exception);
            Assert.IsType(
                expectedExceptionType,
                exception);
        }

        VerifyAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            cancellationToken,
            verifiesCurrentLink: reconciliationAccess is WishlistAccess.Owner);
    }

    [Fact]
    public async Task DeleteAsync_WhenReplacementLinkExistsAfterAmbiguousCommit_ThrowsWishlistShareLinkVersionConflictException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            new TimeoutException(),
            cancellationToken);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(CreateShareLink(wishlistId));

        // Act
        var action = () => _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistShareLinkVersionConflictException>(action);
        VerifyAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            cancellationToken,
            verifiesCurrentLink: true);
    }

    [Fact]
    public async Task DeleteAsync_WhenAmbiguousDeletionReadTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var shareLink = CreateShareLink(wishlistId);
        SetupAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            new TimeoutException(),
            cancellationToken);
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _shareService.DeleteAsync(
            ownerId,
            wishlistId,
            0,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        VerifyAmbiguousDeletion(
            ownerId,
            wishlistId,
            shareLink,
            cancellationToken,
            verifiesCurrentLink: true);
    }

    private static WishlistShareToken CreateToken(
        string secret = "secret",
        byte[]? secretHash = null,
        string protectedSecret = "protected-secret")
    {
        return new WishlistShareToken(
            secret,
            secretHash ??
            [
                1,
                2,
                3
            ],
            protectedSecret);
    }

    private static WishlistShareLink CreateShareLink(
        Guid wishlistId,
        Guid? id = null,
        byte[]? secretHash = null,
        string protectedSecret = "protected-secret")
    {
        return new WishlistShareLink(
            id ?? Guid.CreateVersion7(),
            wishlistId,
            secretHash ??
            [
                1,
                2,
                3
            ],
            protectedSecret);
    }

    private static SharedWishlistDetails CreateSharedWishlist(Guid wishlistId)
    {
        return new SharedWishlistDetails(
            wishlistId,
            "Owner",
            "Wishlist",
            WishlistOccasion.Other,
            null,
            null,
            []);
    }

    private static SharedWishDetail CreateSharedWish()
    {
        return new SharedWishDetail
        {
            Id = Guid.CreateVersion7(),
            Name = "Gift",
            Note = "Public note",
            Url = "https://example.test/gift",
            Price = 12.34m,
            Quantity = 2,
            ReservedQuantity = 1,
            CurrentParticipantReservedQuantity = null
        };
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

    private void SetupOwner(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        _wishlistRepositoryMock
            .Setup(repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
    }

    private void SetupCreate(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _tokenServiceMock
            .Setup(service => service.Create())
            .Returns(CreateToken());
        _shareLinkRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<WishlistShareLink>()));
    }

    private void SetupAmbiguousRotation(
        Guid ownerId,
        Guid wishlistId,
        WishlistShareLink shareLink,
        Exception exception,
        CancellationToken cancellationToken)
    {
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _tokenServiceMock
            .Setup(service => service.Create())
            .Returns(CreateToken(
                "new-secret",
                [
                    4,
                    5,
                    6
                ],
                "new-protected-secret"));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(exception);
    }

    private void SetupAmbiguousDeletion(
        Guid ownerId,
        Guid wishlistId,
        WishlistShareLink shareLink,
        Exception exception,
        CancellationToken cancellationToken)
    {
        SetupOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock
            .Setup(repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _shareLinkRepositoryMock
            .Setup(repository => repository.Remove(shareLink));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(exception);
    }

    private void VerifyOwner(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Once);
    }

    private void VerifyCreate(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken,
        bool verifiesOwner = true,
        bool verifiesReconciliation = false)
    {
        if (verifiesOwner)
        {
            VerifyOwner(
                ownerId,
                wishlistId,
                cancellationToken);
        }

        _tokenServiceMock.Verify(
            service => service.Create(),
            Times.Once);
        _shareLinkRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<WishlistShareLink>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);

        if (verifiesReconciliation)
        {
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetByIdAsync(
                    It.IsAny<Guid>(),
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifySharedLookup(
        WishlistShareLink shareLink,
        string secret,
        CancellationToken cancellationToken,
        bool verifiesContent)
    {
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken),
            Times.Once);
        _tokenServiceMock.Verify(
            service => service.Verify(
                secret,
                shareLink.SecretHash),
            Times.Once);

        if (verifiesContent)
        {
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetSharedWishlistAsync(
                    shareLink.WishlistId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifySharedWishLookup(
        WishlistShareLink shareLink,
        string secret,
        Guid wishId,
        bool verifiesContent,
        CancellationToken cancellationToken)
    {
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                shareLink.Id,
                cancellationToken),
            Times.Once);
        _tokenServiceMock.Verify(
            service => service.Verify(
                secret,
                shareLink.SecretHash),
            Times.Once);

        if (verifiesContent)
        {
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetSharedWishAsync(
                    shareLink.WishlistId,
                    wishId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyRotation(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken,
        bool verifiesToken = false,
        bool verifiesSave = false,
        bool verifiesNoOtherCalls = true,
        int ownerCallCount = 1)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Exactly(ownerCallCount));
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken),
            Times.Once);

        if (verifiesToken)
        {
            _tokenServiceMock.Verify(
                service => service.Create(),
                Times.Once);
        }

        if (verifiesSave)
        {
            _unitOfWorkMock.Verify(
                unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
                Times.Once);
        }

        if (verifiesNoOtherCalls)
            VerifyNoOtherCalls();
    }

    private void VerifyAmbiguousRotation(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken,
        bool verifiesById = false,
        bool verifiesCurrentLink = false,
        bool verifiesReconciliationAccess = true)
    {
        VerifyRotation(
            ownerId,
            wishlistId,
            cancellationToken,
            verifiesToken: true,
            verifiesSave: true,
            verifiesNoOtherCalls: false,
            ownerCallCount: verifiesReconciliationAccess
                ? 2
                : 1);

        if (verifiesById)
        {
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetByIdAsync(
                    It.IsAny<Guid>(),
                    cancellationToken),
                Times.Once);
        }

        if (verifiesCurrentLink)
        {
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetByWishlistIdAsync(
                    wishlistId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyDeletionLookup(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyDeletion(
        Guid ownerId,
        Guid wishlistId,
        WishlistShareLink shareLink,
        CancellationToken cancellationToken,
        bool verifiesSave)
    {
        VerifyOwner(
            ownerId,
            wishlistId,
            cancellationToken);
        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _shareLinkRepositoryMock.Verify(
            repository => repository.Remove(shareLink),
            Times.Once);

        if (verifiesSave)
        {
            _unitOfWorkMock.Verify(
                unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyAmbiguousDeletion(
        Guid ownerId,
        Guid wishlistId,
        WishlistShareLink shareLink,
        CancellationToken cancellationToken,
        bool verifiesCurrentLink = false)
    {
        _wishlistRepositoryMock.Verify(
            repository => repository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken),
            Times.Exactly(2));

        _shareLinkRepositoryMock.Verify(
            repository => repository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _shareLinkRepositoryMock.Verify(
            repository => repository.Remove(shareLink),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);

        if (verifiesCurrentLink)
        {
            _shareLinkRepositoryMock.Verify(
                repository => repository.GetByWishlistIdAsync(
                    wishlistId,
                    cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyNoOtherCalls()
    {
        _shareLinkRepositoryMock.VerifyNoOtherCalls();
        _wishlistRepositoryMock.VerifyNoOtherCalls();
        _tokenServiceMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }
}
