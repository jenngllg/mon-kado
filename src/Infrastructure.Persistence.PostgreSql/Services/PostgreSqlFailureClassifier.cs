using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Classifies PostgreSQL failures that represent temporary unavailability.
/// </summary>
public static class PostgreSqlFailureClassifier
{
    /// <summary>
    /// Executes the is unavailable operation.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The operation result.</returns>
    public static bool IsUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var current = exception;
        while (current is not null)
        {

            if (current is TimeoutException ||
                current is NpgsqlException { IsTransient: true })
            {

                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
