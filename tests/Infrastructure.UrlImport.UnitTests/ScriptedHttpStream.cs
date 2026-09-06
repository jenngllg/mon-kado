namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests;

public class ScriptedHttpStream(byte[] response) : Stream
{
    private readonly MemoryStream _response = new(response);
    public MemoryStream Request { get; } = new();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count) => _response.Read(
        buffer,
        offset,
        count);
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) => _response.ReadAsync(
        buffer,
        cancellationToken);
    public override void Write(
        byte[] buffer,
        int offset,
        int count) => Request.Write(
        buffer,
        offset,
        count);
    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) => Request.WriteAsync(
        buffer,
        cancellationToken);
    public override long Seek(
        long offset,
        SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {

        if (disposing)
        {
            _response.Dispose();
            Request.Dispose();
        }

        base.Dispose(disposing);
    }
}
