namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

[CollectionDefinition(Name)]
public class PostgreSqlWorkerTestSuite : ICollectionFixture<PostgreSqlWorkerFixture>
{
    public const string Name = "PostgreSQL Worker";
}
