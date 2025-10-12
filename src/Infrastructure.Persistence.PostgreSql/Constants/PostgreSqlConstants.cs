using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

/// <summary>
/// Contains constants related to PostgreSQL database schema and table names.
/// </summary>
[ExcludeFromCodeCoverage]
public static class PostgreSqlConstants
{
    /// <summary>
    /// Represents the default schema name used for database operations.
    /// </summary>
    public const string Schema = "public";

    /// <summary>
    /// Represents the name of the database table used to store wishlists.
    /// </summary>
    public const string WishListsTableName = "wish_lists";
}