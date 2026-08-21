using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Moq;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AccountRegistrationServiceTests
{
    [Fact]
    public async Task IgnoreDuplicateAccountAsync_WhenActionSucceeds_Completes()
    {
        // Arrange
        var invoked = false;

        // Act
        await AccountRegistrationService.IgnoreDuplicateAccountAsync(() =>
        {
            invoked = true;

            return Task.CompletedTask;
        });

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public async Task IgnoreDuplicateAccountAsync_WhenAccountIsDuplicate_Completes()
    {
        // Arrange
        var exception = CreateDbUpdateException("ux_users_normalized_email");

        // Act
        var caught = await Record.ExceptionAsync(() =>
            AccountRegistrationService.IgnoreDuplicateAccountAsync(() =>
                Task.FromException(exception)));

        // Assert
        Assert.Null(caught);
    }

    [Fact]
    public async Task IgnoreDuplicateAccountAsync_WhenFailureIsNotDuplicate_PreservesException()
    {
        // Arrange
        var exception = CreateDbUpdateException("ux_other");

        // Act
        Task action() => AccountRegistrationService.IgnoreDuplicateAccountAsync(() =>
            Task.FromException(exception));

        // Assert
        Assert.Same(
            exception,
            await Assert.ThrowsAsync<DbUpdateException>(action));
    }

    [Fact]
    public async Task CanContinueAfterCreationAsync_WhenCreationSucceeds_ReturnsTrue()
    {
        // Arrange
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);

        // Act
        var result = await AccountRegistrationService.CanContinueAfterCreationAsync(
            IdentityResult.Success,
            transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        transactionMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CanContinueAfterCreationAsync_WhenAccountIsDuplicate_RollsBackAndReturnsFalse()
    {
        // Arrange
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        transactionMock
            .Setup(transaction => transaction.RollbackAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        var identityResult = IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail" });

        // Act
        var result = await AccountRegistrationService.CanContinueAfterCreationAsync(
            identityResult,
            transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        transactionMock.Verify(
            transaction => transaction.RollbackAsync(TestContext.Current.CancellationToken),
            Times.Once);
        transactionMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CanContinueAfterCreationAsync_WhenCreationFails_ThrowsDetailedException()
    {
        // Arrange
        var transactionMock = new Mock<IDbContextTransaction>(MockBehavior.Strict);
        var identityResult = IdentityResult.Failed(
            new IdentityError { Code = "First" },
            new IdentityError { Code = "Second" });

        // Act
        Task<bool> action() => AccountRegistrationService.CanContinueAfterCreationAsync(
            identityResult,
            transactionMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>((Func<Task<bool>>)action);
        Assert.Equal(
            "ASP.NET Core Identity rejected account creation: First,Second.",
            exception.Message);
        transactionMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("DuplicateEmail", true)]
    [InlineData("DuplicateUserName", true)]
    [InlineData("PasswordTooShort", false)]
    public void IsDuplicateAccount_WhenIdentityResultIsProvided_ReturnsExpectedResult(
        string errorCode,
        bool expectedResult)
    {
        // Arrange
        var result = IdentityResult.Failed(new IdentityError { Code = errorCode });

        // Act
        var isDuplicate = AccountRegistrationService.IsDuplicateAccount(result);

        // Assert
        Assert.Equal(
            expectedResult,
            isDuplicate);
    }

    [Theory]
    [InlineData("ux_users_normalized_email", true)]
    [InlineData("ux_users_normalized_user_name", true)]
    [InlineData("ux_other", false)]
    [InlineData(null, false)]
    public void IsDuplicateAccount_WhenDatabaseExceptionIsProvided_ReturnsExpectedResult(
        string? constraintName,
        bool expectedResult)
    {
        // Arrange
        var exception = CreateDbUpdateException(constraintName);

        // Act
        var isDuplicate = AccountRegistrationService.IsDuplicateAccount(exception);

        // Assert
        Assert.Equal(
            expectedResult,
            isDuplicate);
    }

    [Fact]
    public void IsDuplicateAccount_WhenInnerExceptionIsNotPostgreSql_ReturnsFalse()
    {
        // Arrange
        var exception = new DbUpdateException(
            "Update failed.",
            new InvalidOperationException());

        // Act
        var isDuplicate = AccountRegistrationService.IsDuplicateAccount(exception);

        // Assert
        Assert.False(isDuplicate);
    }

    private static DbUpdateException CreateDbUpdateException(string? constraintName)
    {
        var innerException = new PostgresException(
            "duplicate",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            null,
            null,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            constraintName,
            "file",
            "1",
            "routine");

        return new DbUpdateException(
            "Update failed.",
            innerException);
    }
}
