namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {

        return now;
    }
}
