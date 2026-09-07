using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.EntityFrameworkCore;

using Moq;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistReportReviewServiceTests
{
    private readonly Mock<IWishlistReportReviewRepository> _repositoryMock = new(MockBehavior.Strict);
    private readonly Mock<IAdministratorAccessService> _accessServiceMock = new(MockBehavior.Strict);
    private readonly Mock<IWishTransactionFactory> _transactionFactoryMock = new(MockBehavior.Strict);
    private readonly Mock<IWishTransaction> _transactionMock = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new(MockBehavior.Strict);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly Guid _administratorId = Guid.CreateVersion7();
    private readonly WishlistReport _report = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        WishlistReportReason.Other,
        "Visitor text");
    private readonly WishlistReportReviewService _service;
    private WishlistReportReviewEvent? _review;
    public WishlistReportReviewServiceTests()
    {
        _service = new WishlistReportReviewService(
            _repositoryMock.Object,
            _accessServiceMock.Object,
            _transactionFactoryMock.Object,
            _unitOfWorkMock.Object,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        _transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenReportCannotBeRead_ReturnsSpecificFailure(bool unavailable)
    {
        // Arrange
        var read = _repositoryMock.Setup(repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken));

        if (unavailable)
            read.ThrowsAsync(new TimeoutException());
        else
            read.ReturnsAsync((WishlistReport?)null);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.GetAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken));

        // Assert
        Assert.IsType(
            unavailable ? typeof(DependencyUnavailableException) : typeof(WishlistReportNotFoundException),
            exception);
        _repositoryMock.Verify(
            repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEventsAsync_WhenReportCannotBeRead_DisposesSnapshotAndReturnsSpecificFailure(bool unavailable)
    {
        // Arrange
        SetupTransaction(IsolationLevel.RepeatableRead);
        var read = _repositoryMock.Setup(repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken));

        if (unavailable)
            read.ThrowsAsync(new TimeoutException());
        else
            read.ReturnsAsync((WishlistReport?)null);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.GetEventsAsync(
                _report.WishlistId,
                _report.Id,
                1,
                20,
                _cancellationToken));

        // Assert
        Assert.IsType(
            unavailable ? typeof(DependencyUnavailableException) : typeof(WishlistReportNotFoundException),
            exception);
        _repositoryMock.Verify(
            repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
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
    public async Task UpdateAsync_WhenAdministratorAccessChanges_StopsBeforeReadingContent(
        bool accountExists,
        AdministratorAccess access,
        Type exceptionType)
    {
        // Arrange
        SetupTransaction(IsolationLevel.ReadCommitted);
        _repositoryMock
            .Setup(repository => repository.LockAdministratorAsync(
                _administratorId,
                _cancellationToken))
            .ReturnsAsync(accountExists);
        _accessServiceMock
            .Setup(service => service.GetAccessAsync(
                _administratorId,
                _cancellationToken))
            .ReturnsAsync(access);

        // Act
        var exception = await Record.ExceptionAsync(UpdateAsync);

        // Assert
        Assert.IsType(
            exceptionType,
            exception);
        _repositoryMock.Verify(
            repository => repository.LockAdministratorAsync(
                _administratorId,
                _cancellationToken),
            Times.Once);
        _accessServiceMock.Verify(
            service => service.GetAccessAsync(
                _administratorId,
                _cancellationToken),
            accountExists ? Times.Once : Times.Never);
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false, 0u, typeof(WishlistReportNotFoundException))]
    [InlineData(true, false, 0u, typeof(WishlistReportNotFoundException))]
    [InlineData(true, true, 1u, typeof(WishlistReportVersionConflictException))]
    public async Task UpdateAsync_WhenResourceOrVersionIsInvalid_DoesNotStageReview(
        bool parentExists,
        bool reportExists,
        uint version,
        Type exceptionType)
    {
        // Arrange
        SetupTransaction(IsolationLevel.ReadCommitted);
        SetupAccess();
        _repositoryMock
            .Setup(repository => repository.LockWishlistAsync(
                _report.WishlistId,
                _cancellationToken))
            .ReturnsAsync(parentExists);
        _repositoryMock
            .Setup(repository => repository.LockReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken))
            .ReturnsAsync(reportExists ? _report : null);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.UpdateAsync(
                _administratorId,
                _report.WishlistId,
                _report.Id,
                WishlistReportStatus.Upheld,
                null,
                version,
                _cancellationToken));

        // Assert
        Assert.IsType(
            exceptionType,
            exception);
        VerifyAccess();
        _repositoryMock.Verify(
            repository => repository.LockWishlistAsync(
                _report.WishlistId,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.LockReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            parentExists ? Times.Once : Times.Never);
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("concurrency", typeof(WishlistReportVersionConflictException))]
    [InlineData("timeout", typeof(DependencyUnavailableException))]
    [InlineData("cancellation", typeof(OperationCanceledException))]
    public async Task UpdateAsync_WhenSaveFails_DoesNotCommitOrReconcileUnpersistedEvent(
        string failure,
        Type exceptionType)
    {
        // Arrange
        SetupUpdate();
        Exception cause = failure switch
        {
            "concurrency" => new DbUpdateConcurrencyException(),
            "timeout" => new TimeoutException(),
            _ => new OperationCanceledException(_cancellationToken)
        };
        _unitOfWorkMock
            .Setup(work => work.SaveChangesAsync(_cancellationToken))
            .ThrowsAsync(cause);

        // Act
        var exception = await Record.ExceptionAsync(UpdateAsync);

        // Assert
        Assert.IsType(
            exceptionType,
            exception);
        VerifyUpdate();
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenReviewIsUnchanged_PreservesStateWithoutSavingOrCreatingEvent()
    {
        // Arrange
        SetupUpdate();
        _report.Review(
            WishlistReportStatus.Upheld,
            "Private note",
            _administratorId,
            DateTime.UnixEpoch);

        // Act
        var result = await UpdateAsync();

        // Assert
        Assert.Equal(
            DateTime.UnixEpoch,
            result.Report.ReviewedAt);
        VerifyAccess();
        _repositoryMock.Verify(
            repository => repository.LockWishlistAsync(
                _report.WishlistId,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.LockReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            Times.Once);
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            false);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task UpdateAsync_WhenCommitAcknowledgementIsLost_ConfirmsOnlyItsStillCurrentDurableEvent(
        int latestKind,
        bool readUnavailable)
    {
        // Arrange
        SetupUpdate();
        SetupTransaction(IsolationLevel.RepeatableRead);
        _unitOfWorkMock
            .Setup(work => work.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);
        _transactionMock
            .SetupSequence(transaction => transaction.CommitAsync(_cancellationToken))
            .ThrowsAsync(new TimeoutException())
            .Returns(Task.CompletedTask);
        var read = _repositoryMock.Setup(repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken));

        if (readUnavailable)
            read.ThrowsAsync(new TimeoutException());
        else
            read.ReturnsAsync(_report);
        _repositoryMock
            .Setup(repository => repository.GetLatestEventAsync(
                _report.Id,
                _cancellationToken))
            .ReturnsAsync(() => latestKind switch
            {
                0 => null,
                1 => new WishlistReportReviewEvent(
                    Guid.CreateVersion7(),
                    _report,
                    2,
                    WishlistReportStatus.Pending),
                _ => Assert.IsType<WishlistReportReviewEvent>(_review)
            });

        // Act
        VersionedWishlistReportDetails? result = null;
        var exception = await Record.ExceptionAsync(async () => result = await UpdateAsync());

        // Assert
        var confirmed = latestKind == 2 && !readUnavailable;

        if (confirmed)
        {
            Assert.Null(exception);
            Assert.NotNull(result);
            Assert.Equal(
                WishlistReportStatus.Upheld,
                result.Report.Status);
        }
        else
            Assert.IsType<DependencyUnavailableException>(exception);
        VerifyUpdate(2);
        _repositoryMock.Verify(
            repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.GetLatestEventAsync(
                _report.Id,
                _cancellationToken),
            readUnavailable ? Times.Never : Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                _cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                _cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(_cancellationToken),
            Times.Exactly(confirmed ? 2 : 1));
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Exactly(2));
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenReportExists_ForwardsCancellationAndReturnsCurrentMetadata()
    {
        // Arrange
        _repositoryMock
            .Setup(repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken))
            .ReturnsAsync(_report);

        // Act
        var result = await _service.GetAsync(
            _report.WishlistId,
            _report.Id,
            _cancellationToken);

        // Assert
        Assert.Equal(
            _report.Id,
            result.Report.Id);
        Assert.Equal(
            WishlistReportStatus.Pending,
            result.Report.Status);
        _repositoryMock.Verify(
            repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetEventsAsync_WhenReportExists_ForwardsCancellationThroughoutSnapshot()
    {
        // Arrange
        SetupTransaction(IsolationLevel.RepeatableRead);
        var page = new WishlistReportReviewEventPage
        {
            CurrentPage = 2,
            PageSize = 10
        };
        _repositoryMock
            .Setup(repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken))
            .ReturnsAsync(_report);
        _repositoryMock
            .Setup(repository => repository.GetEventsAsync(
                _report.Id,
                2,
                10,
                _cancellationToken))
            .ReturnsAsync(page);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(_cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetEventsAsync(
            _report.WishlistId,
            _report.Id,
            2,
            10,
            _cancellationToken);

        // Assert
        Assert.Same(
            page,
            result);
        _repositoryMock.Verify(
            repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.GetEventsAsync(
                _report.Id,
                2,
                10,
                _cancellationToken),
            Times.Once);
        VerifyTransaction(
            IsolationLevel.RepeatableRead,
            true);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAsync_WhenReviewChanges_SavesOneEventAndForwardsCancellationThroughCommit()
    {
        // Arrange
        SetupUpdate();
        _unitOfWorkMock
            .Setup(work => work.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(2);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(_cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await UpdateAsync();

        // Assert
        Assert.Equal(
            WishlistReportStatus.Upheld,
            result.Report.Status);
        Assert.Equal(
            "Private note",
            result.Report.ReviewNote);
        Assert.Equal(
            _administratorId,
            result.Report.ReviewedByAdministratorId);
        VerifyUpdate();
        VerifyTransaction(
            IsolationLevel.ReadCommitted,
            true);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AdministratorAccess.Granted, typeof(WishlistReportNotFoundException))]
    [InlineData(AdministratorAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    [InlineData(AdministratorAccess.Forbidden, typeof(AdministratorAccessDeniedException))]
    public async Task UpdateAsync_WhenCommitIsAmbiguousAndAccessOrResourceDisappears_PreservesSpecificFailure(
        AdministratorAccess access,
        Type expectedException)
    {
        // Arrange
        SetupUpdate();
        SetupTransaction(IsolationLevel.RepeatableRead);
        _accessServiceMock
            .SetupSequence(service => service.GetAccessAsync(
                _administratorId,
                _cancellationToken))
            .ReturnsAsync(AdministratorAccess.Granted)
            .ReturnsAsync(access);
        _unitOfWorkMock
            .Setup(work => work.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(2);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(_cancellationToken))
            .ThrowsAsync(new TimeoutException());
        _repositoryMock
            .Setup(repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken))
            .ReturnsAsync((WishlistReport?)null);

        // Act
        var exception = await Record.ExceptionAsync(UpdateAsync);

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        VerifyUpdate(2);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                _cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                _cancellationToken),
            access is AdministratorAccess.Granted ? Times.Once : Times.Never);
        _repositoryMock.Verify(
            repository => repository.GetReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            access is AdministratorAccess.Granted ? Times.Once : Times.Never);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(_cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Exactly(access is AdministratorAccess.Granted ? 2 : 1));
        VerifyNoOtherCalls();
    }

    private Task<VersionedWishlistReportDetails> UpdateAsync()
    {

        return _service.UpdateAsync(
            _administratorId,
            _report.WishlistId,
            _report.Id,
            WishlistReportStatus.Upheld,
            "Private note",
            0,
            _cancellationToken);
    }

    private void SetupTransaction(IsolationLevel isolation)
    {
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                isolation,
                _cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
    }

    private void SetupAccess()
    {
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
    }

    private void SetupUpdate()
    {
        SetupTransaction(IsolationLevel.ReadCommitted);
        SetupAccess();
        _repositoryMock
            .Setup(repository => repository.LockWishlistAsync(
                _report.WishlistId,
                _cancellationToken))
            .ReturnsAsync(true);
        _repositoryMock
            .Setup(repository => repository.LockReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken))
            .ReturnsAsync(_report);
        _repositoryMock
            .Setup(repository => repository.GetNextSequenceAsync(
                _report.Id,
                _cancellationToken))
            .ReturnsAsync(1);
        _repositoryMock
            .Setup(repository => repository.AddEvent(It.IsAny<WishlistReportReviewEvent>()))
            .Callback<WishlistReportReviewEvent>(review => _review = review);
    }

    private void VerifyAccess(int checks = 1)
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
            Times.Exactly(checks));
    }

    private void VerifyUpdate(int accessChecks = 1)
    {
        VerifyAccess(accessChecks);
        _repositoryMock.Verify(
            repository => repository.LockWishlistAsync(
                _report.WishlistId,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.LockReportAsync(
                _report.WishlistId,
                _report.Id,
                _cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.GetNextSequenceAsync(
                _report.Id,
                _cancellationToken),
            Times.Once);
        var review = Assert.IsType<WishlistReportReviewEvent>(_review);
        Assert.Equal(
            _report.Id,
            review.ReportId);
        Assert.Equal(
            _administratorId,
            review.AdministratorId);
        Assert.Equal(
            7,
            review.Id.Version);
        _repositoryMock.Verify(
            repository => repository.AddEvent(review),
            Times.Once);
        _unitOfWorkMock.Verify(
            work => work.SaveChangesAsync(_cancellationToken),
            Times.Once);
    }

    private void VerifyTransaction(
        IsolationLevel isolation,
        bool committed)
    {
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                isolation,
                _cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(_cancellationToken),
            committed ? Times.Once : Times.Never);
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _repositoryMock.VerifyNoOtherCalls();
        _accessServiceMock.VerifyNoOtherCalls();
        _transactionFactoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }
}
