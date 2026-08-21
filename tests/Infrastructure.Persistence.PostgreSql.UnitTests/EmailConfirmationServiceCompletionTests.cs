using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class EmailConfirmationServiceCompletionTests
{
    private static readonly DateTime _now = new(
        2026,
        8,
        21,
        12,
        0,
        0,
        DateTimeKind.Utc);

    private readonly Mock<IAuthenticationEmailOutboxRepository> _outboxRepositoryMock =
        new(MockBehavior.Strict);
    private readonly Mock<IDbContextTransaction> _transactionMock = new(MockBehavior.Strict);

    [Fact]
    public async Task CompleteConfirmationAsync_WhenIdentitySucceeds_ClosesOutboxAndCommits()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _outboxRepositoryMock
            .Setup(repository => repository.MarkPendingConfirmationMessagesProcessedAsync(
                userId,
                _now,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await EmailConfirmationService.CompleteConfirmationAsync(
            IdentityResult.Success,
            userId,
            _now,
            _outboxRepositoryMock.Object,
            _transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        _outboxRepositoryMock.Verify(repository => repository.MarkPendingConfirmationMessagesProcessedAsync(
            userId,
            _now,
            TestContext.Current.CancellationToken), Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(TestContext.Current.CancellationToken),
            Times.Once);
        _outboxRepositoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteConfirmationAsync_WhenIdentityFails_RollsBackAndReturnsFalse()
    {
        // Arrange
        _transactionMock
            .Setup(transaction => transaction.RollbackAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await EmailConfirmationService.CompleteConfirmationAsync(
            IdentityResult.Failed(new IdentityError { Code = "Failure" }),
            Guid.NewGuid(),
            _now,
            _outboxRepositoryMock.Object,
            _transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        _transactionMock.Verify(
            transaction => transaction.RollbackAsync(TestContext.Current.CancellationToken),
            Times.Once);
        _outboxRepositoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
    }
}
