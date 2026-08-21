namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

internal class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {

        return now;
    }
}
