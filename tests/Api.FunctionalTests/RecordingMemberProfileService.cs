using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records member profile service calls for functional tests.
/// </summary>
public class RecordingMemberProfileService : IMemberProfileService
{
    /// <summary>
    /// Gets the recorded profile update requests.
    /// </summary>
    public List<(Guid MemberId, string DisplayName, uint ExpectedVersion)> Updates { get; } = [];

    /// <summary>
    /// Gets or sets the member profile returned by the fake.
    /// </summary>
    public MemberProfile? MemberProfile
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the exception thrown by the fake.
    /// </summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <inheritdoc />
    public Task<MemberProfile?> UpdateAsync(
        Guid memberId,
        string displayName,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Updates.Add((
            memberId,
            displayName,
            expectedVersion));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(MemberProfile);
    }
}
