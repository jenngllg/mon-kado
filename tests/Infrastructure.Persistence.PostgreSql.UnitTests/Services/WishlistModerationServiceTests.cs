using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.EntityFrameworkCore;

using Moq;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistModerationServiceTests
{
    private readonly Mock<IAdministratorAccessService> _accessServiceMock;
    private readonly Guid _administratorId = Guid.CreateVersion7();
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly Mock<IWishlistModerationRepository> _repositoryMock;
    private readonly WishlistModerationService _service;
    private readonly Mock<IWishTransactionFactory> _transactionFactoryMock;
    private readonly Mock<IWishTransaction> _transactionMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Wishlist _wishlist;
    private WishlistModerationEvent? _decision;
    public WishlistModerationServiceTests()
    {
        _accessServiceMock = new Mock<IAdministratorAccessService>(MockBehavior.Strict);
        _repositoryMock = new Mock<IWishlistModerationRepository>(MockBehavior.Strict);
        _transactionFactoryMock = new Mock<IWishTransactionFactory>(MockBehavior.Strict);
        _transactionMock = new Mock<IWishTransaction>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _wishlist = WishlistModerationTestData.CreateActiveWishlist();
        _service = new WishlistModerationService(
            _repositoryMock.Object,
            _accessServiceMock.Object,
            _transactionFactoryMock.Object,
            _unitOfWorkMock.Object,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenReadCannotResolveWishlist_ReturnsSpecificFailure(bool unavailable)
    {
        // Arrange
        var read = _repositoryMock.Setup(repository => repository.GetWishlistAsync(
                _wishlist.Id,
                _cancellationToken));

        if (unavailable)
            read.ThrowsAsync(new TimeoutException());
        else
            read.ReturnsAsync((Wishlist?)null);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.GetAsync(
                _wishlist.Id,
                _cancellationToken));

        // Assert
        Assert.IsType(
            unavailable ? typeof(DependencyUnavailableException) : typeof(WishlistNotFoundException),
            exception);
        _repositoryMock.Verify(
            repository => repository.GetWishlistAsync(
                _wishlist.Id,
                _cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEventsAsync_WhenReadFails_DisposesConsistentReadTransaction(bool unavailable)
    {
        // Arrange
        SetupTransaction(IsolationLevel.RepeatableRead);
        var read = _repositoryMock.Setup(repository => repository.GetWishlistAsync(
                _wishlist.Id,
                _cancellationToken));

        if (unavailable)
            read.ThrowsAsync(new TimeoutException());
        else
            read.ReturnsAsync((Wishlist?)null);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.GetEventsAsync(
                _wishlist.Id,
                1,
                20,
                _cancellationToken));

        // Assert
        Assert.IsType(
            unavailable ? typeof(DependencyUnavailableException) : typeof(WishlistNotFoundException),
            exception);
        _repositoryMock.Verify(
            repository => repository.GetWishlistAsync(
                _wishlist.Id,
                _cancellationToken),
            Times.Once);
        VerifyTransaction(
            IsolationLevel.RepeatableRead,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, AdministratorAccess.Granted, typeof(InvalidAuthenticationSessionException))]
    [InlineData(true, AdministratorAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(true, AdministratorAccess.Forbidden, typeof(AdministratorAccessDeniedException))]
    public async Task UpdateAsync_WhenAdministratorAccessChanged_StopsBeforeLockingWishlist(
        bool accountExists,
        AdministratorAccess access,
        Type expectedException)
    {
        // Arrange
        SetupTransaction(IsolationLevel.ReadCommitted);
        _repositoryMock
            .Setup(repository => repository.LockAdministratorAsync(
                _administratorId,
                _cancellationToken))
            .ReturnsAsync(accountExists);

        if (accountExists)
            _accessServiceMock
                .Setup(service => service.GetAccessAsync(
                    _administratorId,
                    _cancellationToken))
                .ReturnsAsync(access);

        // Act
        var exception = await Record.ExceptionAsync(UpdateAsync);

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        _repositoryMock.Verify(
            repository => repository.LockAdministratorAsync(
                _administratorId,
                _cancellationToken),
            Times.Once);

        if (accountExists)
            _accessServiceMock.Verify(
                service => service.GetAccessAsync(
                    _administratorId,
                    _cancellationToken),
                Times.Once);
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAsync_WhenWishlistOrVersionChanged_DoesNotStageDecision(bool exists)
    {
        // Arrange
        SetupUpdate(exists ? _wishlist : null);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.UpdateAsync(
                _administratorId,
                _wishlist.Id,
                true,
                "Private reason",
                1,
                _cancellationToken));

        // Assert
        Assert.IsType(
            exists ? typeof(WishlistVersionConflictException) : typeof(WishlistNotFoundException),
            exception);
        VerifyUpdateAccess(1);
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("concurrency", typeof(WishlistVersionConflictException))]
    [InlineData("unavailable", typeof(DependencyUnavailableException))]
    [InlineData("unexpected", typeof(InvalidOperationException))]
    public async Task UpdateAsync_WhenSaveFails_DoesNotAttemptCommitOrReconciliation(
        string scenario,
        Type expectedException)
    {
        // Arrange
        SetupUpdate(_wishlist);
        SetupDecision();
        var failure = scenario switch
        {
            "concurrency" => (Exception)new DbUpdateConcurrencyException(),
            "unavailable" => new TimeoutException(),
            _ => new InvalidOperationException()
        };
        _unitOfWorkMock
            .Setup(unit => unit.SaveChangesAsync(_cancellationToken))
            .ThrowsAsync(failure);

        // Act
        var exception = await Record.ExceptionAsync(UpdateAsync);

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        VerifyUpdateAccess(1);
        VerifyDecision();
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missingEvent", typeof(DependencyUnavailableException))]
    [InlineData("missingWishlist", typeof(WishlistNotFoundException))]
    [InlineData("changedState", typeof(DependencyUnavailableException))]
    [InlineData("changedReason", typeof(DependencyUnavailableException))]
    [InlineData("readUnavailable", typeof(DependencyUnavailableException))]
    public async Task UpdateAsync_WhenAmbiguousResultCannotBeConfirmed_DoesNotClaimSuccess(
        string scenario,
        Type expectedException)
    {
        // Arrange
        SetupUpdate(_wishlist);
        SetupDecision();
        _unitOfWorkMock
            .Setup(unit => unit.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(3);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(_cancellationToken))
            .ThrowsAsync(new TimeoutException());
        var eventRead = _repositoryMock.Setup(repository => repository.GetEventAsync(
                It.IsAny<Guid>(),
                _cancellationToken));

        if (scenario == "readUnavailable")
        {
            eventRead.ThrowsAsync(new TimeoutException());
        }
        else
        {
            eventRead.ReturnsAsync(() => scenario == "missingEvent" ? null : _decision);
            _repositoryMock
                .Setup(repository => repository.GetWishlistAsync(
                    _wishlist.Id,
                    _cancellationToken))
                .ReturnsAsync(() =>
                {

                    if (scenario == "missingWishlist")
                        return null;

                    if (scenario == "changedState")
                        _wishlist.Moderate(
                            false,
                            null,
                            DateTime.UnixEpoch);

                    if (scenario == "changedReason")
                        _wishlist.Moderate(
                            true,
                            "Later reason",
                            DateTime.UnixEpoch);

                    return _wishlist;
                });
        }

        // Act
        var exception = await Record.ExceptionAsync(UpdateAsync);

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        VerifyUpdateAccess(2);
        VerifyDecision();
        _repositoryMock.Verify(
            repository => repository.GetEventAsync(
                Assert
                    .IsType<WishlistModerationEvent>(_decision)
                    .Id,
                _cancellationToken),
            Times.Once);

        if (scenario != "readUnavailable")
            _repositoryMock.Verify(
                repository => repository.GetWishlistAsync(
                    _wishlist.Id,
                    _cancellationToken),
                Times.Once);
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            true);
        VerifyNoOtherCalls();
    }

    private Task<WishlistModerationDetails> UpdateAsync()
    {

        return _service.UpdateAsync(
            _administratorId,
            _wishlist.Id,
            true,
            "Private reason",
            0,
            _cancellationToken);
    }

    private void SetupTransaction(IsolationLevel isolationLevel)
    {
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                isolationLevel,
                _cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        _transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
    }

    private void SetupUpdate(Wishlist? wishlist)
    {
        SetupTransaction(IsolationLevel.ReadCommitted);
        _repositoryMock
            .Setup(repository => repository.LockAdministratorAsync(
                _administratorId,
                _cancellationToken))
            .ReturnsAsync(true);
        _accessServiceMock
            .Setup(service => service.GetAccessAsync(
                _administratorId,
                _cancellationToken))
            .ReturnsAsync(AdministratorAccess.Granted);
        _repositoryMock
            .Setup(repository => repository.LockWishlistAsync(
                _wishlist.Id,
                _cancellationToken))
            .ReturnsAsync(wishlist);
    }

    private void SetupDecision()
    {
        _repositoryMock
            .Setup(repository => repository.GetNextSequenceAsync(
                _wishlist.Id,
                _cancellationToken))
            .ReturnsAsync(1);
        _repositoryMock
            .Setup(repository => repository.AddDecision(It.IsAny<WishlistModerationEvent>()))
            .Callback<WishlistModerationEvent>(decision => _decision = decision);
    }

    private void VerifyTransaction(
        IsolationLevel isolationLevel,
        bool commitAttempted)
    {
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                isolationLevel,
                _cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(_cancellationToken),
            commitAttempted ? Times.Once() : Times.Never());
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
    }

    private void VerifyUpdateAccess(int authorizationReads)
    {
        _repositoryMock.Verify(
            repository => repository.LockAdministratorAsync(
                _administratorId,
                _cancellationToken),
            Times.Once);
        _accessServiceMock.Verify(
            service => service.GetAccessAsync(
                _administratorId,
                _cancellationToken),
            Times.Exactly(authorizationReads));
        _repositoryMock.Verify(
            repository => repository.LockWishlistAsync(
                _wishlist.Id,
                _cancellationToken),
            Times.Once);
    }

    private void VerifyDecision()
    {
        var decision = Assert.IsType<WishlistModerationEvent>(_decision);
        Assert.Equal(
            _administratorId,
            decision.AdministratorId);
        Assert.Equal(
            _wishlist.Id,
            decision.WishlistId);
        _repositoryMock.Verify(
            repository => repository.GetNextSequenceAsync(
                _wishlist.Id,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.AddDecision(decision),
            Times.Once);
        _unitOfWorkMock.Verify(
            unit => unit.SaveChangesAsync(_cancellationToken),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _accessServiceMock.VerifyNoOtherCalls();
        _repositoryMock.VerifyNoOtherCalls();
        _transactionFactoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }
}
