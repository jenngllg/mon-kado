using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records member reservation history service calls for functional tests.
/// </summary>
public class RecordingGiftReservationHistoryService : IGiftReservationHistoryService
{
    /// <summary>Gets recorded history retrievals.</summary>
    public List<(Guid MemberId, int Page, int PageSize, GiftReservationHistoryStatus? Status)> Retrievals { get; } = [];

    /// <summary>Gets or sets the history page returned by retrieval.</summary>
    public GiftReservationHistoryPage Page
    {
        get; set;
    } = new GiftReservationHistoryPage
    {
        CurrentPage = 1,
        PageSize = 20
    };

    /// <summary>Gets or sets an exception thrown by history retrieval.</summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <inheritdoc />
    public Task<GiftReservationHistoryPage?> GetAsync(
        Guid memberId,
        int page,
        int pageSize,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Retrievals.Add((
            memberId,
            page,
            pageSize,
            status));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult<GiftReservationHistoryPage?>(Page);
    }
}
