using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests removal of the current member's profile photo.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="expectedVersion">The expected account version.</param>
public class DeleteProfileImageCommand(
    Guid memberId,
    uint expectedVersion) : IRequest<uint>, IGenericValidationFailure
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
    /// <summary>Gets the expected account version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <inheritdoc/>
    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {

        return new InvalidAuthenticationSessionException();
    }
}

/// <summary>Removes profile photos through the durable deletion outbox.</summary>
/// <param name="profileImageService">The profile-photo persistence service.</param>
/// <param name="logger">The structured logger.</param>
public class DeleteProfileImageCommandHandler(
    IProfileImageService profileImageService,
    ILogger<DeleteProfileImageCommandHandler> logger) : IRequestHandler<DeleteProfileImageCommand, uint>
{
    /// <summary>Deletes the photo reference without requiring the storage volume to be available.</summary>
    /// <param name="request">The validated deletion request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated account version.</returns>
    public async Task<uint> Handle(
        DeleteProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        ProfileImageLogMessages.DeletionStarted(
            logger,
            request.MemberId);
        var profile = await profileImageService.DeleteAsync(
            request.MemberId,
            request.ExpectedVersion,
            cancellationToken);
        ProfileImageLogMessages.Deleted(
            logger,
            request.MemberId);

        return profile.Version;
    }
}
