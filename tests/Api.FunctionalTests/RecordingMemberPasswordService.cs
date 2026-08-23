using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingMemberPasswordService : IMemberPasswordService
{
    public List<(Guid MemberId, string CurrentPassword, string NewPassword)> Changes { get; } = [];

    public bool Result { get; set; } = true;

    public Exception? Exception
    {
        get; set;
    }

    public Task<bool> ChangeAsync(
        Guid memberId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Changes.Add((
            memberId,
            currentPassword,
            newPassword));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(Result);
    }
}
