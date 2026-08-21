namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class CleanupCall(
    DateTime cutoff,
    int batchSize)
{
    public DateTime Cutoff { get; } = cutoff;

    public int BatchSize { get; } = batchSize;
}
