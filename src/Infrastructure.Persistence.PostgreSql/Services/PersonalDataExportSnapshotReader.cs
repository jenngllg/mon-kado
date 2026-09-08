using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;

using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Streams explicit personal-data projections without serializing persistence entities or credentials.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="timeProvider">The UTC snapshot clock.</param>
public class PersonalDataExportSnapshotReader(
    MonKadoDbContext context,
    TimeProvider timeProvider) : IPersonalDataExportSnapshotReader
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false)
        }
    };
    /// <inheritdoc/>
    public async Task<DateTime> WriteAsync(
        Guid memberId,
        Stream dataDestination,
        Stream imageManifest,
        CancellationToken cancellationToken)
    {
        try
        {

            return await WriteSnapshotAsync(
                memberId,
                dataDestination,
                imageManifest,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Owns one repeatable-read transaction for every exported database projection.</summary>
    /// <param name="memberId">The member whose retained data is projected.</param>
    /// <param name="dataDestination">The JSON destination.</param>
    /// <param name="imageManifest">The private image manifest destination.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The UTC database snapshot time.</returns>
    private async Task<DateTime> WriteSnapshotAsync(
        Guid memberId,
        Stream dataDestination,
        Stream imageManifest,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var snapshotAt = timeProvider
            .GetUtcNow()
            .UtcDateTime;
        using var writer = new Utf8JsonWriter(dataDestination);
        using var images = new Utf8JsonWriter(imageManifest);
        writer.WriteStartObject();
        writer.WriteNumber(
            "schemaVersion",
            1);
        writer.WriteString(
            "snapshotAt",
            snapshotAt);
        images.WriteStartArray();
        await WriteAccountAsync(
            memberId,
            writer,
            images,
            cancellationToken);
        await WriteWishlistsAsync(
            memberId,
            writer,
            cancellationToken);
        await WriteWishesAsync(
            memberId,
            writer,
            images,
            cancellationToken);
        await WriteParticipationAsync(
            memberId,
            writer,
            cancellationToken);
        await WriteAccountActivityAsync(
            memberId,
            writer,
            cancellationToken);
        writer.WriteEndObject();
        images.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
        await images.FlushAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return snapshotAt;
    }

    /// <summary>Writes account information and authentication capabilities without credential material.</summary>
    /// <param name="memberId">The trusted member identifier.</param>
    /// <param name="writer">The data document writer.</param>
    /// <param name="images">The private image manifest writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the account projection.</returns>
    private async Task WriteAccountAsync(
        Guid memberId,
        Utf8JsonWriter writer,
        Utf8JsonWriter images,
        CancellationToken cancellationToken)
    {
        var member = await context.Users
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => new
            {
                member.Id,
                member.Email,
                member.EmailConfirmed,
                member.DisplayName,
                member.CreatedAt,
                member.UpdatedAt,
                HasPassword = member.PasswordHash != null,
                member.PhoneNumber,
                member.PhoneNumberConfirmed,
                member.TwoFactorEnabled,
                member.LockoutEnabled,
                member.LockoutEnd,
                member.AccessFailedCount,
                member.ProfileImageId,
                member.ProfileImageHash
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (member is null)
            throw new InvalidAuthenticationSessionException();
        writer.WritePropertyName("account");
        writer.WriteStartObject();
        writer.WritePropertyName("profile");
        JsonSerializer.Serialize(
            writer,
            new
            {
                member.Id,
                member.Email,
                member.EmailConfirmed,
                member.DisplayName,
                member.CreatedAt,
                member.UpdatedAt,
                ImagePath = member.ProfileImageId.HasValue ? PersonalDataExportArchiveNames.GetImagePath(null) : null
            },
            _jsonOptions);
        writer.WritePropertyName("authentication");
        JsonSerializer.Serialize(
            writer,
            new
            {
                member.HasPassword,
                member.PhoneNumber,
                member.PhoneNumberConfirmed,
                member.TwoFactorEnabled,
                member.LockoutEnabled,
                LockoutEnd = member.LockoutEnd.HasValue ? member.LockoutEnd.Value.UtcDateTime : (DateTime?)null,
                member.AccessFailedCount
            },
            _jsonOptions);
        await WriteArrayAsync(
            writer,
            "roles",
            context.UserRoles
                .AsNoTracking()
                .Where(link => link.UserId == memberId)
                .Join(
                context.Roles.AsNoTracking(),
                link => link.RoleId,
                role => role.Id,
                (
                    link,
                    role) => role.Name)
                .OrderBy(name => name),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "externalAccounts",
            context.UserLogins
                .AsNoTracking()
                .Where(login => login.UserId == memberId)
                .OrderBy(login => login.LoginProvider)
                .Select(login => new
                {
                    Provider = login.LoginProvider,
                    ProviderUserId = login.ProviderKey,
                    login.ProviderDisplayName
                }),
            cancellationToken);
        writer.WriteEndObject();
        await WriteImageReferenceAsync(
            images,
            member.ProfileImageId,
            null,
            member.ProfileImageHash,
            cancellationToken);
    }

    /// <summary>Writes owned list content and sharing metadata without any sharing capability.</summary>
    /// <param name="memberId">The trusted owner identifier.</param>
    /// <param name="writer">The data document writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the list projections.</returns>
    private async Task WriteWishlistsAsync(
        Guid memberId,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        await WriteArrayAsync(
            writer,
            "wishlists",
            context.Wishlists
                .AsNoTracking()
                .Where(wishlist => wishlist.OwnerId == memberId)
                .OrderBy(wishlist => wishlist.Id)
                .Select(wishlist => new
                {
                    wishlist.Id,
                    wishlist.Name,
                    wishlist.Occasion,
                    wishlist.EventDate,
                    wishlist.Message,
                    wishlist.CreatedAt,
                    wishlist.UpdatedAt,
                    wishlist.IsSuspended,
                    wishlist.SuspensionReason,
                    wishlist.SuspendedAt
                }),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "sharing",
            context.WishlistShareLinks
                .AsNoTracking()
                .Where(link => context.Wishlists.Any(wishlist => wishlist.Id == link.WishlistId && wishlist.OwnerId == memberId))
                .OrderBy(link => link.WishlistId)
                .Select(link => new
                {
                    link.Id,
                    link.WishlistId,
                    link.CreatedAt,
                    link.UpdatedAt
                }),
            cancellationToken);
    }

    /// <summary>Streams owned wishes and spools only the image references required by this snapshot.</summary>
    /// <param name="memberId">The trusted owner identifier.</param>
    /// <param name="writer">The data document writer.</param>
    /// <param name="images">The private disk-backed image manifest writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the complete wish projection.</returns>
    private async Task WriteWishesAsync(
        Guid memberId,
        Utf8JsonWriter writer,
        Utf8JsonWriter images,
        CancellationToken cancellationToken)
    {
        writer.WriteStartArray("wishes");
        var query = context.Wishes
            .AsNoTracking()
            .Where(wish => context.Wishlists.Any(wishlist => wishlist.Id == wish.WishlistId && wishlist.OwnerId == memberId))
            .OrderBy(wish => wish.WishlistId)
            .ThenBy(wish => wish.Position)
            .ThenBy(wish => wish.Id)
            .Select(wish => new
            {
                wish.Id,
                wish.WishlistId,
                wish.Name,
                wish.Note,
                wish.Url,
                wish.Price,
                wish.Quantity,
                wish.Position,
                wish.CreatedAt,
                wish.UpdatedAt,
                wish.ImageId,
                wish.ImageContentHash
            });
        await foreach (var wish in query
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            JsonSerializer.Serialize(
                writer,
                new
                {
                    wish.Id,
                    wish.WishlistId,
                    wish.Name,
                    wish.Note,
                    wish.Url,
                    wish.Price,
                    wish.Quantity,
                    wish.Position,
                    wish.CreatedAt,
                    wish.UpdatedAt,
                    ImagePath = wish.ImageId.HasValue ? PersonalDataExportArchiveNames.GetImagePath(wish.Id) : null
                },
                _jsonOptions);
            await writer.FlushAsync(cancellationToken);
            await WriteImageReferenceAsync(
                images,
                wish.ImageId,
                wish.Id,
                wish.ImageContentHash,
                cancellationToken);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes only the member's own participation and reservation lifecycles.</summary>
    /// <param name="memberId">The trusted member identifier.</param>
    /// <param name="writer">The data document writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing participant-owned data.</returns>
    private async Task WriteParticipationAsync(
        Guid memberId,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        await WriteArrayAsync(
            writer,
            "participations",
            context.WishlistParticipants
                .AsNoTracking()
                .Where(participant => participant.MemberId == memberId)
                .OrderBy(participant => participant.Id)
                .Select(participant => new
                {
                    participant.Id,
                    participant.WishlistId,
                    participant.GuestDisplayName,
                    participant.CreatedAt,
                    participant.UpdatedAt
                }),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "reservations",
            context.GiftReservations
                .AsNoTracking()
                .Where(reservation => context.WishlistParticipants.Any(participant => participant.Id == reservation.WishlistParticipantId && participant.MemberId == memberId))
                .OrderBy(reservation => reservation.Id)
                .Select(reservation => new
                {
                    reservation.Id,
                    reservation.WishlistId,
                    reservation.WishId,
                    reservation.WishlistParticipantId,
                    reservation.Quantity,
                    reservation.CreatedAt,
                    reservation.UpdatedAt
                }),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "reservationHistory",
            context.GiftReservationHistories
                .AsNoTracking()
                .Where(history => history.MemberId == memberId)
                .OrderBy(history => history.CreatedAt)
                .ThenBy(history => history.Id)
                .Select(history => new
                {
                    history.Id,
                    history.WishlistId,
                    history.WishlistName,
                    history.WishId,
                    history.WishName,
                    history.Quantity,
                    history.Status,
                    history.CreatedAt,
                    history.LastActivityAt,
                    history.EndedAt
                }),
            cancellationToken);
    }

    /// <summary>Writes retained security activity without hashes, stamps, credentials or raw delivery diagnostics.</summary>
    /// <param name="memberId">The trusted member identifier.</param>
    /// <param name="writer">The data document writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the activity projections.</returns>
    private async Task WriteAccountActivityAsync(
        Guid memberId,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        writer.WriteStartObject("accountActivity");
        await WriteArrayAsync(
            writer,
            "sessions",
            context.AuthenticationSessions
                .AsNoTracking()
                .Where(session => session.UserId == memberId)
                .OrderBy(session => session.CreatedAt)
                .ThenBy(session => session.Id)
                .Select(session => new
                {
                    session.Id,
                    session.IsPersistent,
                    session.CreatedAt,
                    session.RenewedAt,
                    session.ExpiresAt,
                    session.RevokedAt
                }),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "emailChangeRequests",
            context.MemberEmailChangeRequests
                .AsNoTracking()
                .Where(request => request.UserId == memberId)
                .OrderBy(request => request.CreatedAt)
                .ThenBy(request => request.Id)
                .Select(request => new
                {
                    request.Id,
                    request.CurrentEmail,
                    request.NewEmail,
                    request.CreatedAt,
                    request.ExpiresAt,
                    request.ConfirmedAt,
                    request.RevokedAt
                }),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "deletionRequests",
            context.MemberAccountDeletionRequests
                .AsNoTracking()
                .Where(request => request.MemberId == memberId)
                .OrderBy(request => request.CreatedAt)
                .ThenBy(request => request.Id)
                .Select(request => new
                {
                    request.Id,
                    request.Email,
                    request.CreatedAt,
                    request.ExpiresAt
                }),
            cancellationToken);
        await WriteArrayAsync(
            writer,
            "notifications",
            context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .Where(message => message.UserId == memberId)
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Select(message => new
                {
                    message.Id,
                    message.Kind,
                    message.RecipientEmail,
                    message.CreatedAt,
                    message.ProcessedAt,
                    message.AttemptCount,
                    HasDeliveryFailure = message.LastError != null
                }),
            cancellationToken);
        writer.WriteEndObject();
    }

    /// <summary>Flushes explicit projections progressively rather than collecting a complete table in memory.</summary>
    /// <typeparam name="T">The explicit projection type, never an EF entity.</typeparam>
    /// <param name="writer">The data document writer.</param>
    /// <param name="name">The stable collection property name.</param>
    /// <param name="query">The already filtered, ordered and projected query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the complete streamed collection.</returns>
    private static async Task WriteArrayAsync<T>(
        Utf8JsonWriter writer,
        string name,
        IQueryable<T> query,
        CancellationToken cancellationToken)
    {
        writer.WriteStartArray(name);
        await foreach (var item in query
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            JsonSerializer.Serialize(
                writer,
                item,
                _jsonOptions);
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray();
    }

    /// <summary>Spools one immutable image reference without placing its digest in the exported JSON.</summary>
    /// <param name="images">The private manifest writer.</param>
    /// <param name="imageId">The current image identifier, when present.</param>
    /// <param name="wishId">The owned wish identifier, or null for a profile image.</param>
    /// <param name="contentHash">The expected normalized image digest.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the spooled reference.</returns>
    private static async Task WriteImageReferenceAsync(
        Utf8JsonWriter images,
        Guid? imageId,
        Guid? wishId,
        byte[]? contentHash,
        CancellationToken cancellationToken)
    {

        if (imageId is not { } identifier)
            return;
        // The archive builder validates the digest against the copied bytes before publication.
        JsonSerializer.Serialize(
            images,
            new PersonalDataExportImage
            {
                ImageId = identifier,
                WishId = wishId,
                ContentHash = contentHash
            },
            _jsonOptions);
        await images.FlushAsync(cancellationToken);
    }
}
