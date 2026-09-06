using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Defines bounded profile-photo logs containing technical identifiers only.</summary>
public static partial class ProfileImageLogMessages
{
    /// <summary>Logs profile-photo UpsertStarted events.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="memberId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ProfileImageUpsertStarted, Level = LogLevel.Debug, Message = "Profile image update started for member {MemberId}")]
    public static partial void UpsertStarted(
        ILogger logger,
        Guid memberId);
    /// <summary>Logs profile-photo Upserted events.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="memberId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ProfileImageUpserted, Level = LogLevel.Information, Message = "Profile image updated for member {MemberId}")]
    public static partial void Upserted(
        ILogger logger,
        Guid memberId);
    /// <summary>Logs profile-photo DeletionStarted events.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="memberId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ProfileImageDeletionStarted, Level = LogLevel.Debug, Message = "Profile image deletion started for member {MemberId}")]
    public static partial void DeletionStarted(
        ILogger logger,
        Guid memberId);
    /// <summary>Logs profile-photo Deleted events.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="memberId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ProfileImageDeleted, Level = LogLevel.Information, Message = "Profile image deleted for member {MemberId}")]
    public static partial void Deleted(
        ILogger logger,
        Guid memberId);
    /// <summary>Logs profile-photo ReconciliationDeferred events.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="imageId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ProfileImageReconciliationDeferred, Level = LogLevel.Error, Message = "Profile image reconciliation deferred for image {ImageId}")]
    public static partial void ReconciliationDeferred(
        ILogger logger,
        Guid imageId);
    /// <summary>Logs profile-photo Read events.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="memberId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ProfileImageRead, Level = LogLevel.Information, Message = "Profile image read for member {MemberId}")]
    public static partial void Read(
        ILogger logger,
        Guid memberId);
}
