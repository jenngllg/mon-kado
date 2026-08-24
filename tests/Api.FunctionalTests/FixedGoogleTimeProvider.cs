namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class FixedGoogleTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _utcNow = now;

    public override DateTimeOffset GetUtcNow()
    {

        return _utcNow;
    }

    public void Advance(TimeSpan elapsed)
    {
        _utcNow = _utcNow.Add(elapsed);
    }
}
