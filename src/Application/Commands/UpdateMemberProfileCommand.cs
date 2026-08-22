using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to update the current member profile.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="displayName">The requested display name.</param>
/// <param name="expectedVersion">The profile version supplied by the client.</param>
public class UpdateMemberProfileCommand(
    Guid memberId,
    string? displayName,
    uint expectedVersion) : IRequest<MemberProfile>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>
    /// Gets the requested display name.
    /// </summary>
    public string? DisplayName { get; } = displayName;

    /// <summary>
    /// Gets the profile version supplied by the client.
    /// </summary>
    public uint ExpectedVersion { get; } = expectedVersion;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles current member profile updates.
/// </summary>
/// <param name="memberProfileService">The member profile service.</param>
/// <param name="logger">The logger.</param>
public class UpdateMemberProfileCommandHandler(
    IMemberProfileService memberProfileService,
    ILogger<UpdateMemberProfileCommandHandler> logger)
    : IRequestHandler<UpdateMemberProfileCommand, MemberProfile>
{
    /// <summary>
    /// Updates the current member profile.
    /// </summary>
    /// <param name="request">The profile update command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated member profile.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    public async Task<MemberProfile> Handle(
        UpdateMemberProfileCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.MemberProfileUpdateStarted(
            logger,
            request.MemberId);
        var profile = await memberProfileService.UpdateAsync(
            request.MemberId,
            request.DisplayName?.Trim() ?? string.Empty,
            request.ExpectedVersion,
            cancellationToken);

        if (profile is null)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.MemberProfileUpdated(
            logger,
            request.MemberId);

        return profile;
    }
}
