namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

internal class StepTimeProvider(DateTimeOffset currentTime) : TimeProvider
{
    private readonly object _lock = new();
    private DateTimeOffset _currentTime = currentTime;
    private int _advanceOnRead;
    private int _readCount;
    private TimeSpan _advanceDuration;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            _readCount++;

            if (_readCount == _advanceOnRead)
                _currentTime = _currentTime.Add(_advanceDuration);

            return _currentTime;
        }
    }

    public void AdvanceOnRead(
        int readNumber,
        TimeSpan duration)
    {
        lock (_lock)
        {
            _advanceOnRead = readNumber;
            _advanceDuration = duration;
            _readCount = 0;
        }
    }
}
