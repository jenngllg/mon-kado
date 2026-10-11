using AutoFixture;

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

using Npgsql;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistSubscriptionServiceTests
{
    private readonly Mock<IWishlistSubscriptionRepository> _repositoryMock = new(MockBehavior.Strict);
    private readonly Mock<IWishlistShareLinkRepository> _linksMock = new(MockBehavior.Strict);
    private readonly Mock<IWishlistRepository> _wishlistsMock = new(MockBehavior.Strict);
    private readonly Mock<IWishlistShareTokenService> _tokensMock = new(MockBehavior.Strict);
    private readonly Mock<IWishTransactionFactory> _transactionsMock = new(MockBehavior.Strict);
    private readonly Mock<IWishTransaction> _transactionMock = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new(MockBehavior.Strict);
    private readonly WishlistSubscriptionService _service;
    private readonly Guid _memberId = Guid.CreateVersion7();
    private readonly Wishlist _wishlist;
    private readonly WishlistShareLink _link;
    private readonly WishlistSubscriptionDetails _details;
    private const string Secret = "presented-secret";

    public WishlistSubscriptionServiceTests()
    {
        var fixture = TestFixture.Create();
        fixture.Register(() => new DateOnly(
            2026,
            12,
            24));
        _wishlist = new Wishlist(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            fixture.Create<string>(),
            fixture.Create<string>(),
            WishlistOccasion.Other,
            null,
            null);
        _link = new WishlistShareLink(
            Guid.CreateVersion7(),
            _wishlist.Id,
            new byte[32],
            fixture.Create<string>());
        _details = fixture.Create<WishlistSubscriptionDetails>();
        _transactionMock.Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        _service = new WishlistSubscriptionService(
            _repositoryMock.Object,
            _linksMock.Object,
            _wishlistsMock.Object,
            _tokensMock.Object,
            _transactionsMock.Object,
            _unitOfWorkMock.Object);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("link")]
    [InlineData("secret")]
    [InlineData("list")]
    [InlineData("self")]
    [InlineData("existing")]
    [InlineData("unique")]
    [InlineData("summary")]
    [InlineData("unavailable")]
    [InlineData("unexpected")]
    public async Task CreateAsync_WhenStateChanges_UsesLocksAndCommitsOnlyAValidSubscription(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberId = scenario == "self" ? _wishlist.OwnerId : _memberId;
        var reachesSecret = scenario is not ("link" or "unavailable" or "unexpected");
        var reachesList = reachesSecret && scenario != "secret";
        var reachesExisting = reachesList && scenario is not ("list" or "self");
        var reachesSave = reachesExisting && scenario != "existing";
        var reachesSummary = reachesSave && scenario != "unique";
        var succeeds = scenario == "success";
        _transactionsMock.Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        var linkSetup = _linksMock.Setup(repository => repository.LockActiveAsync(
            _link.Id,
            cancellationToken));

        if (scenario == "unavailable")
            linkSetup.ThrowsAsync(new TimeoutException());

        if (scenario == "unexpected")
            linkSetup.ThrowsAsync(new InvalidOperationException());

        if (scenario is not ("unavailable" or "unexpected"))
            linkSetup.ReturnsAsync(scenario == "link" ? null : _link);

        if (reachesSecret)
            _tokensMock.Setup(tokens => tokens.Verify(
                    Secret,
                    _link.SecretHash))
                .Returns(scenario != "secret");

        if (reachesList)
            _wishlistsMock.Setup(repository => repository.GetByIdAsync(
                    _wishlist.Id,
                    cancellationToken))
                .ReturnsAsync(scenario == "list" ? null : _wishlist);

        if (reachesExisting)
            _repositoryMock.Setup(repository => repository.GetCurrentAsync(
                    memberId,
                    _link.Id,
                    cancellationToken))
                .ReturnsAsync(scenario == "existing" ? _details : null);

        if (reachesSave)
        {
            _repositoryMock.Setup(repository => repository.Add(It.Is<WishlistSubscription>(subscription =>
                subscription.Id.Version == 7 &&
                subscription.MemberId == memberId &&
                subscription.WishlistId == _wishlist.Id &&
                subscription.ShareLinkId == _link.Id &&
                subscription.ShareSecretHash.SequenceEqual(_link.SecretHash))));
            var save = _unitOfWorkMock.Setup(unit => unit.SaveChangesAsync(cancellationToken));

            if (scenario == "unique")
                save.ThrowsAsync(new DbUpdateException(
                    "duplicate",
                    new PostgresException(
                        "duplicate",
                        "ERROR",
                        "ERROR",
                        PostgresErrorCodes.UniqueViolation,
                        constraintName: "ux_wishlist_subscriptions_member_wishlist")));

            if (scenario != "unique")
                save.ReturnsAsync(1);
        }

        if (reachesSummary)
            _repositoryMock.Setup(repository => repository.GetAsync(
                    memberId,
                    It.Is<Guid>(id => id.Version == 7),
                    cancellationToken))
                .ReturnsAsync(scenario == "summary" ? null : _details);

        if (succeeds)
            _transactionMock.Setup(transaction => transaction.CommitAsync(cancellationToken))
                .Returns(Task.CompletedTask);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.CreateAsync(
            memberId,
            _link.Id,
            Secret,
            cancellationToken));

        // Assert
        if (succeeds)
            Assert.Null(exception);

        if (scenario is "link" or "secret" or "list")
            Assert.IsType<SharedWishlistNotFoundException>(exception);

        if (scenario == "self")
            Assert.IsType<WishlistSubscriptionSelfException>(exception);

        if (scenario is "existing" or "unique")
            Assert.IsType<WishlistSubscriptionAlreadyExistsException>(exception);

        if (scenario == "summary")
            Assert.IsType<WishlistSubscriptionNotFoundException>(exception);

        if (scenario == "unavailable")
            Assert.IsType<DependencyUnavailableException>(exception);

        if (scenario == "unexpected")
            Assert.IsType<InvalidOperationException>(exception);

        _transactionsMock.Verify(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken),
            Times.Once);
        _linksMock.Verify(repository => repository.LockActiveAsync(
                _link.Id,
                cancellationToken),
            Times.Once);

        if (reachesSecret)
            _tokensMock.Verify(tokens => tokens.Verify(
                    Secret,
                    _link.SecretHash),
                Times.Once);

        if (reachesList)
            _wishlistsMock.Verify(repository => repository.GetByIdAsync(
                    _wishlist.Id,
                    cancellationToken),
                Times.Once);

        if (reachesExisting)
            _repositoryMock.Verify(repository => repository.GetCurrentAsync(
                    memberId,
                    _link.Id,
                    cancellationToken),
                Times.Once);

        if (reachesSave)
        {
            _repositoryMock.Verify(repository => repository.Add(It.IsAny<WishlistSubscription>()),
                Times.Once);
            _unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(cancellationToken),
                Times.Once);
        }

        if (reachesSummary)
            _repositoryMock.Verify(repository => repository.GetAsync(
                    memberId,
                    It.Is<Guid>(id => id.Version == 7),
                    cancellationToken),
                Times.Once);

        if (succeeds)
            _transactionMock.Verify(transaction => transaction.CommitAsync(cancellationToken),
                Times.Once);

        _transactionMock.Verify(transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_WhenReading_ForwardsMemberAndCancellationOrMapsUnavailability(bool unavailable)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = _repositoryMock.Setup(repository => repository.GetAsync(
            _memberId,
            _details.Id,
            cancellationToken));

        if (unavailable)
            setup.ThrowsAsync(new TimeoutException());

        if (!unavailable)
            setup.ReturnsAsync(_details);

        // Act
        if (unavailable)
            await Assert.ThrowsAsync<DependencyUnavailableException>(() => _service.GetAsync(
                _memberId,
                _details.Id,
                cancellationToken));

        if (!unavailable)
            Assert.Same(
                _details,
                await _service.GetAsync(
                    _memberId,
                    _details.Id,
                    cancellationToken));

        // Assert
        _repositoryMock.Verify(repository => repository.GetAsync(
                _memberId,
                _details.Id,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("link")]
    [InlineData("secret")]
    [InlineData("unavailable")]
    [InlineData("unexpected")]
    public async Task GetCurrentAsync_WhenReading_VerifiesCapabilityAndForwardsCancellation(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transactionsMock.Setup(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        var setup = _linksMock.Setup(repository => repository.LockActiveAsync(
            _link.Id,
            cancellationToken));

        if (scenario == "unavailable")
            setup.ThrowsAsync(new TimeoutException());

        if (scenario == "unexpected")
            setup.ThrowsAsync(new InvalidOperationException());

        if (scenario is not ("unavailable" or "unexpected"))
            setup.ReturnsAsync(scenario == "link" ? null : _link);

        if (scenario is "success" or "secret")
            _tokensMock.Setup(tokens => tokens.Verify(
                    Secret,
                    _link.SecretHash))
                .Returns(scenario == "success");

        if (scenario == "success")
        {
            _repositoryMock.Setup(repository => repository.GetCurrentAsync(
                    _memberId,
                    _link.Id,
                    cancellationToken))
                .ReturnsAsync(_details);
            _transactionMock.Setup(transaction => transaction.CommitAsync(cancellationToken))
                .Returns(Task.CompletedTask);
        }

        // Act
        var exception = await Record.ExceptionAsync(() => _service.GetCurrentAsync(
            _memberId,
            _link.Id,
            Secret,
            cancellationToken));

        // Assert
        if (scenario == "success")
            Assert.Null(exception);

        if (scenario is "link" or "secret")
            Assert.IsType<SharedWishlistNotFoundException>(exception);

        if (scenario == "unavailable")
            Assert.IsType<DependencyUnavailableException>(exception);

        if (scenario == "unexpected")
            Assert.IsType<InvalidOperationException>(exception);

        _transactionsMock.Verify(factory => factory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken),
            Times.Once);
        _linksMock.Verify(repository => repository.LockActiveAsync(
                _link.Id,
                cancellationToken),
            Times.Once);

        if (scenario is "success" or "secret")
            _tokensMock.Verify(tokens => tokens.Verify(
                    Secret,
                    _link.SecretHash),
                Times.Once);

        if (scenario == "success")
        {
            _repositoryMock.Verify(repository => repository.GetCurrentAsync(
                    _memberId,
                    _link.Id,
                    cancellationToken),
                Times.Once);
            _transactionMock.Verify(transaction => transaction.CommitAsync(cancellationToken),
                Times.Once);
        }

        _transactionMock.Verify(transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPageAsync_WhenReading_ForwardsPaginationOrMapsUnavailability(bool unavailable)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = new WishlistSubscriptionPage();
        var setup = _repositoryMock.Setup(repository => repository.GetPageAsync(
            _memberId,
            2,
            20,
            cancellationToken));

        if (unavailable)
            setup.ThrowsAsync(new TimeoutException());

        if (!unavailable)
            setup.ReturnsAsync(page);

        // Act
        if (unavailable)
            await Assert.ThrowsAsync<DependencyUnavailableException>(() => _service.GetPageAsync(
                _memberId,
                2,
                20,
                cancellationToken));

        if (!unavailable)
            Assert.Same(
                page,
                await _service.GetPageAsync(
                    _memberId,
                    2,
                    20,
                    cancellationToken));

        // Assert
        _repositoryMock.Verify(repository => repository.GetPageAsync(
                _memberId,
                2,
                20,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("concurrent")]
    [InlineData("unavailable")]
    public async Task DeleteAsync_WhenRemoving_IsMemberScopedAndDoesNotReplay(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subscription = new WishlistSubscription(
            _details.Id,
            _memberId,
            _wishlist.Id,
            _link.Id,
            _link.SecretHash);
        var setup = _repositoryMock.Setup(repository => repository.GetForUpdateAsync(
            _memberId,
            _details.Id,
            cancellationToken));

        if (scenario == "unavailable")
            setup.ThrowsAsync(new TimeoutException());

        if (scenario != "unavailable")
            setup.ReturnsAsync(scenario == "missing" ? null : subscription);

        if (scenario is "success" or "concurrent")
        {
            _repositoryMock.Setup(repository => repository.Remove(subscription));
            var save = _unitOfWorkMock.Setup(unit => unit.SaveChangesAsync(cancellationToken));

            if (scenario == "success")
                save.ReturnsAsync(1);

            if (scenario == "concurrent")
                save.ThrowsAsync(new DbUpdateConcurrencyException());
        }

        // Act
        if (scenario == "unavailable")
            await Assert.ThrowsAsync<DependencyUnavailableException>(() => _service.DeleteAsync(
                _memberId,
                _details.Id,
                cancellationToken));

        if (scenario != "unavailable")
            Assert.Equal(
                scenario == "success",
                await _service.DeleteAsync(
                    _memberId,
                    _details.Id,
                    cancellationToken));

        // Assert
        _repositoryMock.Verify(repository => repository.GetForUpdateAsync(
                _memberId,
                _details.Id,
                cancellationToken),
            Times.Once);

        if (scenario is "success" or "concurrent")
        {
            _repositoryMock.Verify(repository => repository.Remove(subscription),
                Times.Once);
            _unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(cancellationToken),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    private void VerifyNoOtherCalls()
    {
        _repositoryMock.VerifyNoOtherCalls();
        _linksMock.VerifyNoOtherCalls();
        _wishlistsMock.VerifyNoOtherCalls();
        _tokensMock.VerifyNoOtherCalls();
        _transactionsMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }
}
