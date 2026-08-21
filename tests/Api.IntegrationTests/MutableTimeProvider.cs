namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

internal class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
{
    private DateTimeOffset _currentTime = currentTime;

    public override DateTimeOffset GetUtcNow()
    {

        return _currentTime;
    }

    public void Advance(TimeSpan duration)
    {
        _currentTime = _currentTime.Add(duration);
    }
}
