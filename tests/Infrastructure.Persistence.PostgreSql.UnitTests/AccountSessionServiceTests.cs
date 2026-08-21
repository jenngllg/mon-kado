using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AccountSessionServiceTests
{
    [Fact]
    public async Task CommitIfUserIsMissingAsync_WhenUserExists_ReturnsFalse()
    {
        // Arrange
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);

        // Act
        var result = await AccountSessionService.CommitIfUserIsMissingAsync(
            new MonKadoUser(),
            transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        transactionMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommitIfUserIsMissingAsync_WhenUserIsMissing_CommitsAndReturnsTrue()
    {
        // Arrange
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        transactionMock
            .Setup(transaction => transaction.CommitAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await AccountSessionService.CommitIfUserIsMissingAsync(
            null,
            transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        transactionMock.Verify(
            transaction => transaction.CommitAsync(TestContext.Current.CancellationToken),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void EnsureIdentityUpdateSucceeded_WhenResultSucceeds_Completes()
    {
        // Arrange
        var result = IdentityResult.Success;

        // Act
        AccountSessionService.EnsureIdentityUpdateSucceeded(
            result,
            "update account");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void EnsureIdentityUpdateSucceeded_WhenResultFails_ThrowsDetailedException()
    {
        // Arrange
        var result = IdentityResult.Failed(
            new IdentityError { Code = "First" },
            new IdentityError { Code = "Second" });

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AccountSessionService.EnsureIdentityUpdateSucceeded(
                result,
                "update account"));

        // Assert
        Assert.Equal(
            "Unable to update account: First, Second.",
            exception.Message);
    }
}
