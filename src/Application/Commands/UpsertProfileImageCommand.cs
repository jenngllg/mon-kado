using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests addition or replacement of the current member's profile photo.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="image">The untrusted image bytes.</param>
/// <param name="expectedVersion">The expected account version.</param>
/// <param name="hasValidMultipartShape">Whether exactly one image field was supplied.</param>
public class UpsertProfileImageCommand(
    Guid memberId,
    byte[]? image,
    uint expectedVersion,
    bool hasValidMultipartShape) : IRequest<MemberProfile>, IGenericValidationFailure
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
    /// <summary>Gets the untrusted image bytes.</summary>
    public byte[]? Image { get; } = image;
    /// <summary>Gets the expected account version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;
    /// <summary>Gets whether the multipart request contains exactly one image field.</summary>
    public bool HasValidMultipartShape { get; } = hasValidMultipartShape;

    /// <inheritdoc/>
    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>Normalizes and durably attaches profile photos without retaining originals.</summary>
/// <param name="processor">The profile-photo processor.</param>
/// <param name="store">The shared durable image store.</param>
/// <param name="profileImageService">The profile-photo persistence service.</param>
/// <param name="timeProvider">The UTC clock.</param>
/// <param name="logger">The structured logger.</param>
public class UpsertProfileImageCommandHandler(
    IProfileImageProcessor processor,
    IGiftImageStore store,
    IProfileImageService profileImageService,
    TimeProvider timeProvider,
    ILogger<UpsertProfileImageCommandHandler> logger) : IRequestHandler<UpsertProfileImageCommand, MemberProfile>
{
    /// <summary>Stores normalized bytes and commits the account reference before reconciling the marker.</summary>
    /// <param name="request">The validated upload request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated profile.</returns>
    public async Task<MemberProfile> Handle(
        UpsertProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        ProfileImageLogMessages.UpsertStarted(
            logger,
            request.MemberId);
        var processed = await processor.ProcessAsync(
            request.Image,
            cancellationToken);
        var imageId = Guid.CreateVersion7(timeProvider.GetUtcNow());
        await store.WritePendingAsync(
            imageId,
            processed.Content,
            cancellationToken);
        var profile = await profileImageService.UpsertAsync(
            request.MemberId,
            imageId,
            processed.ContentHash,
            request.ExpectedVersion,
            cancellationToken);
        await ReconcileAsync(
            imageId,
            profile.ProfileImageId.GetValueOrDefault() == imageId,
            cancellationToken);
        ProfileImageLogMessages.Upserted(
            logger,
            request.MemberId);

        return profile;
    }

    /// <summary>Confirms a committed file or removes a duplicate without exposing storage diagnostics.</summary>
    /// <param name="imageId">The newly stored image identifier.</param>
    /// <param name="wasCommitted">Whether PostgreSQL references the new image.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing marker reconciliation.</returns>
    private async Task ReconcileAsync(
        Guid imageId,
        bool wasCommitted,
        CancellationToken cancellationToken)
    {
        try
        {

            if (wasCommitted)
            {
                await store.MarkCommittedAsync(
                    imageId,
                    cancellationToken);

                return;
            }

            await store.DeleteAsync(
                imageId,
                cancellationToken);
        }
        catch (GiftImageStorageUnavailableException)
        {
            ProfileImageLogMessages.ReconciliationDeferred(
                logger,
                imageId);
        }
    }
}
