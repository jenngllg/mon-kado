namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

internal class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
