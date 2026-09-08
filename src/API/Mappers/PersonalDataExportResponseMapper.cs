using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.Mappers;

/// <summary>Maps shared export lifecycle metadata without exposing administrative audit references.</summary>
public static class PersonalDataExportResponseMapper
{
    /// <summary>Maps lifecycle metadata to the exact public contract.</summary>
    /// <param name="details">The owned lifecycle details.</param>
    /// <returns>The public response with a bounded terminal failure code.</returns>
    public static PersonalDataExportResponse Map(PersonalDataExportDetails details)
    {

        return new PersonalDataExportResponse
        {
            Id = details.Id,
            Status = details.Status,
            CreatedAt = details.CreatedAt,
            SnapshotAt = details.SnapshotAt,
            ReadyAt = details.ReadyAt,
            ExpiresAt = details.ExpiresAt,
            SizeInBytes = details.SizeInBytes,
            ErrorCode = details.Failure switch
            {
                PersonalDataExportFailure.GenerationFailed => ErrorCodes.MemberDataExportGenerationFailed,
                PersonalDataExportFailure.TooLarge => ErrorCodes.MemberDataExportTooLarge,
                _ => null
            }
        };
    }
}
