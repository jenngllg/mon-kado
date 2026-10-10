using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Represents the CreateWishlistSubscriptionCommand operation.</summary>
/// <param name="memberId">The memberId.</param>
/// <param name="shareLinkId">The shareLinkId.</param>
/// <param name="secret">The secret.</param>
public class CreateWishlistSubscriptionCommand(
    Guid memberId,
    Guid shareLinkId,
    string? secret) : IRequest<WishlistSubscriptionDetails>, IGenericValidationFailure
{
    /// <summary>Gets the memberId.</summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>Gets the shareLinkId.</summary>
    public Guid ShareLinkId { get; } = shareLinkId;

    /// <summary>Gets the secret.</summary>
    public string? Secret { get; } = secret;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(IEnumerable<ValidationError> validationErrors)
    {

        return new SharedWishlistNotFoundException();
    }
}

/// <summary>Handles the subscription operation without affecting gift participation.</summary>
/// <param name="service">The subscription service.</param>
/// <param name="logger">The structured logger.</param>
public class CreateWishlistSubscriptionCommandHandler(
    IWishlistSubscriptionService service,
    ILogger<CreateWishlistSubscriptionCommandHandler> logger) : IRequestHandler<CreateWishlistSubscriptionCommand, WishlistSubscriptionDetails>
{
    /// <summary>Executes the validated subscription operation.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<WishlistSubscriptionDetails> Handle(
        CreateWishlistSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            request.MemberId,
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            cancellationToken);
        WishlistSubscriptionLogMessages.Created(
            logger,
            request.MemberId,
            result.Id);

        return result;
    }
}
