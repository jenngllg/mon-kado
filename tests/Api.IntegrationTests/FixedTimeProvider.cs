namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

internal class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {

        return now;
    }
}
