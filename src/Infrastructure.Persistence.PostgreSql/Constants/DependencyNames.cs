using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

/// <summary>Defines stable technical dependency identifiers for persistence failures.</summary>
[ExcludeFromCodeCoverage]
public static class DependencyNames
{
    /// <summary>Identifies the PostgreSQL dependency.</summary>
    public const string PostgreSql = "PostgreSQL";
}
