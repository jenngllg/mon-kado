using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Injects one save failure while retaining a real PostgreSQL transaction.</summary>
public class ProfileImageSaveFailureInterceptor : SaveChangesInterceptor
{
    private Exception? _failure;
    /// <summary>Arms the next save with the selected failure.</summary>
    /// <param name="failure">The exception to inject once.</param>
    public void Arm(Exception failure)
    {
        Interlocked.Exchange(
            ref _failure,
            failure);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken)
    {
        var failure = Interlocked.Exchange(
            ref _failure,
            null);

        if (failure is not null)
            throw failure;

        return ValueTask.FromResult(result);
    }
}
