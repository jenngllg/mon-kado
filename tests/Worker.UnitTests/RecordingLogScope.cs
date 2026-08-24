namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class RecordingLogScope : IDisposable
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
