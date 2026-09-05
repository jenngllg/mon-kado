using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore.Storage;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistReportTransactionTests
{
    [Fact]
    public async Task CommitAsync_WhenCalled_ForwardsCancellationToken()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        var transaction = new WishlistReportTransaction(transactionMock.Object);
        transactionMock
            .Setup(transaction => transaction.CommitAsync(cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await transaction.CommitAsync(cancellationToken);

        // Assert
        transactionMock.Verify(
            transaction => transaction.CommitAsync(cancellationToken),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisposeAsync_WhenCalled_DisposesInnerTransaction()
    {
        // Arrange
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        var transaction = new WishlistReportTransaction(transactionMock.Object);
        transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        // Act
        await transaction.DisposeAsync();

        // Assert
        transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
    }
}
