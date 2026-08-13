using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

internal static class PostgreSqlFailureClassifier
{
    public static bool IsUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception? current = exception;
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
