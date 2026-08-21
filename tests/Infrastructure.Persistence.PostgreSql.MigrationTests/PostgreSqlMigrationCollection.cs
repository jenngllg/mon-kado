namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.MigrationTests;

[CollectionDefinition(Name)]
public class PostgreSqlMigrationTestSuite : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "PostgreSQL migrations";
}
