using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records anonymous wishlist report calls for functional tests.
/// </summary>
public class RecordingWishlistReportService : IWishlistReportService
{
    /// <summary>Gets the reported wishlist identifier returned by the fake.</summary>
    public Guid WishlistId
    {
        get; set;
    } = Guid.CreateVersion7();

    /// <summary>Gets recorded report creations.</summary>
    public List<WishlistReportCall> Creations
    {
        get;
    } = [];

    /// <summary>Gets or sets the exception thrown by the fake.</summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <inheritdoc />
    public Task<Guid> CreateAsync(
        Guid reportId,
        Guid shareLinkId,
        string shareSecret,
        WishlistReportReason reason,
        string? details,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Creations.Add(new WishlistReportCall(
            reportId,
            shareLinkId,
            shareSecret,
            reason,
            details));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(WishlistId);
    }
}
