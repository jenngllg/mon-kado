using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class AdministrativeAuditServiceTests : IDisposable
{
    private readonly Mock<IWishTransactionFactory> _transactionFactoryMock;
    private readonly MonKadoDbContext _context;
    private readonly AdministrativeAuditService _service;

    public AdministrativeAuditServiceTests()
    {
        _transactionFactoryMock = new Mock<IWishTransactionFactory>(MockBehavior.Strict);
        _context = new MonKadoDbContext(new DbContextOptions<MonKadoDbContext>());
        _service = new AdministrativeAuditService(
            _context,
            _transactionFactoryMock.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPageAsync_WhenTransactionFails_TranslatesOnlyUnavailableAndForwardsCancellation(bool cancelled)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var failure = cancelled ? (Exception)new OperationCanceledException(ct) : new TimeoutException("Database unavailable");
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                ct))
            .ThrowsAsync(failure);

        // Act
        var actual = await Record.ExceptionAsync(() => _service.GetPageAsync(
            new AdministrativeAuditFilter
            {
                Page = 1,
                PageSize = 20
            },
            ct));

        // Assert

        if (cancelled)
            Assert.Same(
                failure,
                actual);
        else
        {
            var unavailable = Assert.IsType<DependencyUnavailableException>(actual);
            Assert.Same(
                failure,
                unavailable.InnerException);
        }
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(
                IsolationLevel.RepeatableRead,
                ct),
            Times.Once);
        _transactionFactoryMock.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
