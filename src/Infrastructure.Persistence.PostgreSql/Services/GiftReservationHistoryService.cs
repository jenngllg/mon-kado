using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Retrieves durable member reservation history from PostgreSQL.
/// </summary>
/// <param name="giftReservationRepository">The gift reservation repository.</param>
public class GiftReservationHistoryService(
    IGiftReservationRepository giftReservationRepository) : IGiftReservationHistoryService
{
    /// <inheritdoc />
    public async Task<GiftReservationHistoryPage?> GetAsync(
        Guid memberId,
        int page,
        int pageSize,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken)
    {
        try
        {
            var memberExists = await giftReservationRepository.MemberExistsAsync(
                memberId,
                cancellationToken);

            if (!memberExists)
                return null;

            var totalCount = await giftReservationRepository.CountHistoryAsync(
                memberId,
                status,
                cancellationToken);
            var offset = (long)(page - 1) * pageSize;
            var items = offset < totalCount
                ? await giftReservationRepository.GetHistoryPageAsync(
                    memberId,
                    status,
                    (int)offset,
                    pageSize,
                    cancellationToken)
                : [];

            return new GiftReservationHistoryPage
            {
                Items = items,
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
