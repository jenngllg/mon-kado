using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

/// <summary>
/// Represents the configuration settings required to connect to a PostgreSQL database.
/// </summary>
[ExcludeFromCodeCoverage]
public class PostgreSqlConfiguration
{
    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    [Required]
    public string? ConnectionString { get; set; }
}