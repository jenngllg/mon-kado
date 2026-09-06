using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>Records profile-photo persistence calls without connecting to PostgreSQL.</summary>
public class RecordingProfileImageService : IProfileImageService
{
    public Guid? ImageId
    {
        get; private set;
    }
    public Guid MemberId
    {
        get; private set;
    }
    public Exception? Exception
    {
        get; set;
    }
    public int UpsertCount
    {
        get; private set;
    }

    /// <inheritdoc/>
    public Task<MemberProfile> UpsertAsync(
        Guid memberId,
        Guid imageId,
        byte[] contentHash,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Exception is not null)
            throw Exception;
        MemberId = memberId;
        ImageId = imageId;
        UpsertCount++;

        return Task.FromResult(new MemberProfile(
                "Jennifer",
                expectedVersion + 1)
        {
            ProfileImageId = imageId
        });
    }

    /// <inheritdoc/>
    public Task<MemberProfile> DeleteAsync(
        Guid memberId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Exception is not null)
            throw Exception;
        ImageId = null;

        return Task.FromResult(new MemberProfile(
                "Jennifer",
                expectedVersion + 1));
    }

    /// <inheritdoc/>
    public Task<bool> IsCurrentAsync(
        Guid memberId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(MemberId == memberId && ImageId == imageId);
    }
}
