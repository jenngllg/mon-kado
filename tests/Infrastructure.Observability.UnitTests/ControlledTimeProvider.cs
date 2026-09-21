namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests;

public class ControlledTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(
        2026,
        9,
        21,
        12,
        0,
        0,
        TimeSpan.Zero);
    private long _timestamp;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan elapsed)
    {
        _now += elapsed;
        _timestamp += elapsed.Ticks;
    }
}
