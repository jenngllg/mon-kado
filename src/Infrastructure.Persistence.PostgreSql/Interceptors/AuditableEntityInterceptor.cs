using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Interceptors;
/// <summary>
/// Represents auditable entity interceptor.
/// </summary>
/// <param name="timeProvider">The time provider.</param>

public class AuditableEntityInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <summary>
    /// Executes the saving changes operation.
    /// </summary>
    /// <param name="eventData">The event data.</param>
    /// <param name="result">The result.</param>
    /// <returns>The operation result.</returns>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditValues(eventData.Context);

        return base.SavingChanges(
            eventData,
            result);
    }
    /// <summary>
    /// Executes the saving changes async operation.
    /// </summary>
    /// <param name="eventData">The event data.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken)
    {
        ApplyAuditValues(eventData.Context);

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    internal void ApplyAuditValues(DbContext? context)
    {

        if (context is null)
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entries = context.ChangeTracker.Entries<IAuditableEntity>();
        foreach (var entry in entries)
        {

            if (entry.State == EntityState.Added)
            {
                SetCurrentValue(
                    entry,
                    nameof(IAuditableEntity.CreatedAt),
                    now);
                SetCurrentValue<DateTime?>(
                    entry,
                    nameof(IAuditableEntity.UpdatedAt),
                    null);
            }

            if (entry.State == EntityState.Modified)
            {
                SetCurrentValue<DateTime?>(
                    entry,
                    nameof(IAuditableEntity.UpdatedAt),
                    now);
            }
        }
    }

    private static void SetCurrentValue<TValue>(
        EntityEntry entry,
        string propertyName,
        TValue value)
    {
        entry.Property(propertyName).CurrentValue = value;
    }
}
