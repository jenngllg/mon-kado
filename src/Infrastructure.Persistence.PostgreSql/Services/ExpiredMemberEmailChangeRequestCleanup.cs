using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Removes expired and retained completed member email change requests.
/// </summary>
/// <param name="requestRepository">The member email change request repository.</param>
public class ExpiredMemberEmailChangeRequestCleanup(
    IMemberEmailChangeRequestRepository requestRepository)
    : IExpiredMemberEmailChangeRequestCleanup
{
    private static readonly TimeSpan _completedRequestRetention = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public async Task<int> DeleteExpiredRequestsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return await requestRepository.DeleteExpiredOrCompletedAsync(
            now,
            now.Subtract(_completedRequestRetention),
            batchSize,
            cancellationToken);
    }
}
