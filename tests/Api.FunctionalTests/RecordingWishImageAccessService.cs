using JennGllg.Fr.MonKado.Back.Application.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records current-state gift-image access checks for functional tests.
/// </summary>
public class RecordingWishImageAccessService : IWishImageAccessService
{
    /// <summary>Gets recorded private image access checks.</summary>
    public List<(
        Guid OwnerId,
        Guid WishlistId,
        Guid WishId,
        Guid ImageId)> OwnedChecks
    {
        get;
    } = [];

    /// <summary>Gets recorded shared image access checks.</summary>
    public List<(
        Guid ShareLinkId,
        Guid WishlistId,
        Guid WishId,
        Guid ImageId)> SharedChecks
    {
        get;
    } = [];

    /// <summary>Gets or sets whether private image access remains current.</summary>
    public bool IsOwnedCurrent
    {
        get; set;
    } = true;

    /// <summary>Gets or sets whether shared image access remains current.</summary>
    public bool IsSharedCurrent
    {
        get; set;
    } = true;

    /// <inheritdoc />
    public Task<bool> IsOwnedImageCurrentAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OwnedChecks.Add((
            ownerId,
            wishlistId,
            wishId,
            imageId));

        return Task.FromResult(IsOwnedCurrent);
    }

    /// <inheritdoc />
    public Task<bool> IsSharedImageCurrentAsync(
        Guid shareLinkId,
        Guid wishlistId,
        Guid wishId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedChecks.Add((
            shareLinkId,
            wishlistId,
            wishId,
            imageId));

        return Task.FromResult(IsSharedCurrent);
    }
}
