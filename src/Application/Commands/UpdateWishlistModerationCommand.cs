using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests an administrator-only suspension or reactivation.</summary>
/// <param name="administratorId">The authenticated administrator identifier.</param>
/// <param name="wishlistId">The wishlist identifier.</param>
/// <param name="isSuspended">The requested suspension state.</param>
/// <param name="reason">The private suspension reason.</param>
/// <param name="expectedVersion">The version supplied by the administrator.</param>
public class UpdateWishlistModerationCommand(
    Guid administratorId,
    Guid wishlistId,
    bool? isSuspended,
    string? reason,
    uint expectedVersion) : IRequest<WishlistModerationDetails>
{
    /// <summary>Gets the administrator identifier.</summary>
    public Guid AdministratorId { get; } = administratorId;
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the requested suspension state.</summary>
    public bool? IsSuspended { get; } = isSuspended;
    /// <summary>Gets the incoming private reason.</summary>
    public string? Reason { get; } = reason;
    /// <summary>Gets the required concurrency version.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;
}

/// <summary>Coordinates validated moderation decisions without logging private reasons.</summary>
/// <param name="service">The moderation service.</param>
/// <param name="logger">The structured logger.</param>
public class UpdateWishlistModerationCommandHandler(
    IWishlistModerationService service,
    ILogger<UpdateWishlistModerationCommandHandler> logger) : IRequestHandler<UpdateWishlistModerationCommand, WishlistModerationDetails>
{
    /// <summary>Applies a validated administrator decision.</summary>
    /// <param name="request">The validated decision.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting moderation state.</returns>
    public async Task<WishlistModerationDetails> Handle(
        UpdateWishlistModerationCommand request,
        CancellationToken cancellationToken)
    {
        WishlistModerationLogMessages.UpdateStarted(
            logger,
            request.AdministratorId,
            request.WishlistId);
        var reason = request.Reason?.Trim()
            .Normalize(NormalizationForm.FormC);
        var result = await service.UpdateAsync(
            request.AdministratorId,
            request.WishlistId,
            request.IsSuspended.GetValueOrDefault(),
            reason,
            request.ExpectedVersion,
            cancellationToken);
        WishlistModerationLogMessages.Updated(
            logger,
            request.AdministratorId,
            request.WishlistId);

        return result;
    }
}
