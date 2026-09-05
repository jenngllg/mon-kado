namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Represents a durable request to delete one obsolete gift image.
/// </summary>
public class GiftImageDeletionOutboxMessage
{
    private GiftImageDeletionOutboxMessage()
    {
    }

    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>
    /// Gets the obsolete image identifier.
    /// </summary>
    public Guid ImageId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the UTC creation date and time.
    /// </summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the next UTC processing date and time.
    /// </summary>
    public DateTime AvailableAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets the number of processing attempts.
    /// </summary>
    public int AttemptCount
    {
        get; private set;
    }

    /// <summary>
    /// Gets the optional UTC lease expiration.
    /// </summary>
    public DateTime? LockedUntil
    {
        get; private set;
    }

    /// <summary>
    /// Creates a deletion request for an obsolete image.
    /// </summary>
    /// <param name="imageId">The obsolete image identifier.</param>
    /// <param name="createdAt">The UTC creation date and time.</param>
    /// <returns>The created outbox message.</returns>
    public static GiftImageDeletionOutboxMessage Create(
        Guid imageId,
        DateTime createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(
            imageId,
            Guid.Empty);

        return new GiftImageDeletionOutboxMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAt)),
            ImageId = imageId,
            CreatedAt = createdAt,
            AvailableAt = createdAt
        };
    }

    /// <summary>
    /// Claims this message until the supplied UTC date and time.
    /// </summary>
    /// <param name="lockedUntil">The lease expiration.</param>
    public void Claim(DateTime lockedUntil)
    {
        AttemptCount++;
        LockedUntil = lockedUntil;
    }

    /// <summary>
    /// Reschedules this message after a failed deletion.
    /// </summary>
    /// <param name="availableAt">The next UTC attempt date and time.</param>
    public void ScheduleRetry(DateTime availableAt)
    {
        AvailableAt = availableAt;
        LockedUntil = null;
    }
}
