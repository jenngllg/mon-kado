using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class ReportedWishlistServiceTests : IDisposable
{
    private readonly Mock<IWishTransactionFactory> _transactionFactoryMock = new(MockBehavior.Strict);
    private readonly Mock<IGiftImageStore> _imageStoreMock = new(MockBehavior.Strict);
    private readonly MonKadoDbContext _context = new(new DbContextOptions<MonKadoDbContext>());
    private readonly ReportedWishlistService _service;
    public ReportedWishlistServiceTests()
    {
        _service = new ReportedWishlistService(
            _context,
            _transactionFactoryMock.Object,
            _imageStoreMock.Object);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadAsync_WhenTransactionCannotStart_TranslatesAvailabilityAndForwardsCancellation(int operation)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var exception = new TimeoutException("PostgreSQL timeout");
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            .ThrowsAsync(exception);

        // Act
        var action = operation switch
        {
            0 => () => _service.GetPageAsync(
                null,
                null,
                1,
                20,
                cancellationToken),
            1 => () => _service.GetAsync(
                Guid.CreateVersion7(),
                cancellationToken),
            2 => () => _service.GetReportsAsync(
                Guid.CreateVersion7(),
                null,
                1,
                20,
                cancellationToken),
            _ => (Func<Task>)(() => _service.OpenImageAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                cancellationToken))
        };
        var actual = await Assert.ThrowsAsync<DependencyUnavailableException>(action);

        // Assert
        Assert.Same(
            exception,
            actual.InnerException);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken),
            Times.Once);
        _transactionFactoryMock.VerifyNoOtherCalls();
        _imageStoreMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadAsync_WhenCancelled_DoesNotTranslateCancellationToAvailability()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var exception = new OperationCanceledException(cancellationToken);
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            .ThrowsAsync(exception);

        // Act
        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => _service.GetPageAsync(
                null,
                null,
                1,
                20,
                cancellationToken));

        // Assert
        Assert.Same(
            exception,
            actual);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken),
            Times.Once);
        _transactionFactoryMock.VerifyNoOtherCalls();
        _imageStoreMock.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
