using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Persists anonymous wishlist reports in PostgreSQL.
/// </summary>
public class WishlistReportService : IWishlistReportService
{
    private readonly IWishlistReportRepository _reportRepository;
    private readonly IWishlistReportTransactionFactory _transactionFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWishlistShareTokenService _wishlistShareTokenService;

    /// <summary>
    /// Initializes a wishlist report service.
    /// </summary>
    /// <param name="reportRepository">The report repository.</param>
    /// <param name="transactionFactory">The report transaction factory.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="wishlistShareTokenService">The share-link token service.</param>
    public WishlistReportService(
        IWishlistReportRepository reportRepository,
        IWishlistReportTransactionFactory transactionFactory,
        IUnitOfWork unitOfWork,
        IWishlistShareTokenService wishlistShareTokenService)
    {
        _reportRepository = reportRepository;
        _transactionFactory = transactionFactory;
        _unitOfWork = unitOfWork;
        _wishlistShareTokenService = wishlistShareTokenService;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(
        Guid reportId,
        Guid shareLinkId,
        string shareSecret,
        WishlistReportReason reason,
        string? details,
        CancellationToken cancellationToken)
    {
        Guid wishlistId;

        try
        {
            await using var transaction = await _transactionFactory.BeginAsync(cancellationToken);
            var shareLink = await _transactionFactory.LockShareLinkAsync(
                shareLinkId,
                cancellationToken);

            if (shareLink is null || !_wishlistShareTokenService.Verify(
                    shareSecret,
                    shareLink.SecretHash))
                throw new SharedWishlistNotFoundException();

            var report = new WishlistReport(
                reportId,
                shareLink.WishlistId,
                reason,
                details);
            _reportRepository.Add(report);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            wishlistId = shareLink.WishlistId;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        return wishlistId;
    }
}
