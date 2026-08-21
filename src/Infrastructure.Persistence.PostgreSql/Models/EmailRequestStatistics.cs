using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
/// <summary>
/// Represents email request statistics.
/// </summary>
/// <param name="count">The count.</param>
/// <param name="latestRequestAt">The latest request at.</param>

[ExcludeFromCodeCoverage]
public class EmailRequestStatistics(
    int count,
    DateTime latestRequestAt)
{
    /// <summary>
    /// Gets count.
    /// </summary>
    public int Count { get; } = count;
    /// <summary>
    /// Gets latest request at.
    /// </summary>

    public DateTime LatestRequestAt { get; } = latestRequestAt;
}
