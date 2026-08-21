namespace JennGllg.Fr.MonKado.Back.Domain.Abstractions;

/// <summary>
/// Defines the UTC audit timestamps maintained for a persisted entity.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>
    /// Gets the UTC date and time when the entity was created.
    /// </summary>
    DateTime CreatedAt
    {
        get;
    }

    /// <summary>
    /// Gets the UTC date and time when the entity was last updated, or <see langword="null" /> when it has not been updated.
    /// </summary>
    DateTime? UpdatedAt
    {
        get;
    }
}
