using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistReportServiceTests
{
    private readonly Mock<IWishlistReportRepository> _reportRepositoryMock;
    private readonly WishlistReportService _service;
    private readonly Mock<IWishlistReportTransactionFactory> _transactionFactoryMock;
    private readonly Mock<IWishlistReportTransaction> _transactionMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IWishlistShareTokenService> _wishlistShareTokenServiceMock;

    public WishlistReportServiceTests()
    {
        _reportRepositoryMock = new Mock<IWishlistReportRepository>(MockBehavior.Strict);
        _transactionFactoryMock = new Mock<IWishlistReportTransactionFactory>(MockBehavior.Strict);
        _transactionMock = new Mock<IWishlistReportTransaction>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _wishlistShareTokenServiceMock = new Mock<IWishlistShareTokenService>(MockBehavior.Strict);
        _transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        _service = new WishlistReportService(
            _reportRepositoryMock.Object,
            _transactionFactoryMock.Object,
            _unitOfWorkMock.Object,
            _wishlistShareTokenServiceMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WhenShareLinkIsValid_PersistsAnonymousReport()
    {
        // Arrange
        var reportId = Guid.CreateVersion7();
        var shareLinkId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var shareLink = CreateShareLink(
            shareLinkId,
            wishlistId);
        var cancellationToken = TestContext.Current.CancellationToken;
        WishlistReport? addedReport = null;
        SetupTransaction(
            shareLink,
            cancellationToken);
        _wishlistShareTokenServiceMock
            .Setup(service => service.Verify(
                "secret",
                shareLink.SecretHash))
            .Returns(true);
        _reportRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<WishlistReport>()))
            .Callback<WishlistReport>(report => addedReport = report);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateAsync(
            reportId,
            shareLinkId,
            "secret",
            WishlistReportReason.InappropriateContent,
            "Details",
            cancellationToken);

        // Assert
        Assert.Equal(
            wishlistId,
            result);
        Assert.NotNull(addedReport);
        Assert.Equal(
            reportId,
            addedReport.Id);
        Assert.Equal(
            wishlistId,
            addedReport.WishlistId);
        Assert.Equal(
            WishlistReportReason.InappropriateContent,
            addedReport.Reason);
        Assert.Equal(
            "Details",
            addedReport.Details);
        VerifyTransaction(
            shareLinkId,
            commits: true,
            cancellationToken);
        _wishlistShareTokenServiceMock.Verify(
            service => service.Verify(
                "secret",
                shareLink.SecretHash),
            Times.Once);
        _reportRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<WishlistReport>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_WhenShareLinkOrSecretIsInvalid_ThrowsNotFound(bool shareLinkExists)
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var shareLink = shareLinkExists
            ? CreateShareLink(
                shareLinkId,
                Guid.CreateVersion7())
            : null;
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupTransaction(
            shareLink,
            cancellationToken);

        if (shareLink is not null)
        {
            _wishlistShareTokenServiceMock
                .Setup(service => service.Verify(
                    "invalid",
                    shareLink.SecretHash))
                .Returns(false);
        }

        // Act
        var action = () => _service.CreateAsync(
            Guid.CreateVersion7(),
            shareLinkId,
            "invalid",
            WishlistReportReason.SpamOrScam,
            null,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        VerifyTransaction(
            shareLinkId,
            commits: false,
            cancellationToken);

        if (shareLink is not null)
        {
            _wishlistShareTokenServiceMock.Verify(
                service => service.Verify(
                    "invalid",
                    shareLink.SecretHash),
                Times.Once);
        }

        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _service.CreateAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "secret",
            WishlistReportReason.SpamOrScam,
            null,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenUnexpectedFailureOccurs_PreservesException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedException = new InvalidOperationException();
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ThrowsAsync(expectedException);

        // Act
        var action = () => _service.CreateAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "secret",
            WishlistReportReason.SpamOrScam,
            null,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expectedException,
            exception);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private static WishlistShareLink CreateShareLink(
        Guid shareLinkId,
        Guid wishlistId)
    {

        return new WishlistShareLink(
            shareLinkId,
            wishlistId,
            new byte[32],
            "protected-secret");
    }

    private void SetupTransaction(
        WishlistShareLink? shareLink,
        CancellationToken cancellationToken)
    {
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        _transactionFactoryMock
            .Setup(factory => factory.LockShareLinkAsync(
                It.IsAny<Guid>(),
                cancellationToken))
            .ReturnsAsync(shareLink);
    }

    private void VerifyTransaction(
        Guid shareLinkId,
        bool commits,
        CancellationToken cancellationToken)
    {
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockShareLinkAsync(
                shareLinkId,
                cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(cancellationToken),
            commits
                ? Times.Once()
                : Times.Never());
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _reportRepositoryMock.VerifyNoOtherCalls();
        _transactionFactoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
        _wishlistShareTokenServiceMock.VerifyNoOtherCalls();
    }
}
