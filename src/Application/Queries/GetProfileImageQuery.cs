using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests the current public photo of a confirmed account.</summary>
/// <param name="memberId">The public member identifier.</param>
/// <param name="imageId">The versioned image identifier from the URL.</param>
public class GetProfileImageQuery(
    Guid memberId,
    Guid? imageId) : IRequest<Stream>, IGenericValidationFailure
{
    /// <summary>Gets the public member identifier.</summary>
    public Guid MemberId { get; } = memberId;
    /// <summary>Gets the requested image identifier.</summary>
    public Guid? ImageId { get; } = imageId;

    /// <inheritdoc/>
    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {

        return new ProfileImageNotFoundException();
    }
}

/// <summary>Revalidates public photo references before opening the shared image store.</summary>
/// <param name="profileImageService">The current-state photo service.</param>
/// <param name="store">The shared image store.</param>
/// <param name="logger">The structured logger.</param>
public class GetProfileImageQueryHandler(
    IProfileImageService profileImageService,
    IGiftImageStore store,
    ILogger<GetProfileImageQueryHandler> logger) : IRequestHandler<GetProfileImageQuery, Stream>
{
    /// <summary>Opens a currently referenced photo without accepting historical image identifiers.</summary>
    /// <param name="request">The validated photo request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable stream, owned by the caller.</returns>
    /// <exception cref="ProfileImageNotFoundException">The account or image is no longer publicly available.</exception>
    /// <exception cref="GiftImageStorageUnavailableException">A referenced file is unavailable.</exception>
    public async Task<Stream> Handle(
        GetProfileImageQuery request,
        CancellationToken cancellationToken)
    {
        var imageId = request.ImageId.GetValueOrDefault();
        var isCurrent = await profileImageService.IsCurrentAsync(
            request.MemberId,
            imageId,
            cancellationToken);

        if (!isCurrent)
            throw new ProfileImageNotFoundException();
        var stream = await store.OpenReadAsync(
            imageId,
            cancellationToken);

        if (stream is null)
        {

            throw new GiftImageStorageUnavailableException(new FileNotFoundException("A referenced profile image is missing."));
        }

        ProfileImageLogMessages.Read(
            logger,
            request.MemberId);

        return stream;
    }
}
