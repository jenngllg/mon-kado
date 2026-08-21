namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[CollectionDefinition(Name)]
public class PostgreSqlApiTestSuite : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "PostgreSQL API";
}
