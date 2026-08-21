namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class TemporaryKeyDirectory : IDisposable
{
    public TemporaryKeyDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mon-kado-data-protection-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path
    {
        get;
    }

    public void Dispose()
    {

        if (Directory.Exists(Path))
            Directory.Delete(
                Path,
                recursive: true);
    }
}
