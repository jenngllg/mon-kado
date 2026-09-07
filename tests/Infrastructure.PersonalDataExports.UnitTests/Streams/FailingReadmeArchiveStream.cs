using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Streams;

/// <summary>Simulates a storage write failure when the ZIP starts its README entry.</summary>
public class FailingReadmeArchiveStream : MemoryStream
{
    private static readonly byte[] _entryName = Encoding.UTF8.GetBytes("README.txt");
    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {

        if (buffer.SequenceEqual(_entryName))
            throw new IOException("private-path");
        base.Write(buffer);
    }

    /// <inheritdoc/>
    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        var content = buffer.AsSpan(
            offset,
            count);

        if (content.SequenceEqual(_entryName))
            throw new IOException("private-path");
        base.Write(
            buffer,
            offset,
            count);
    }
}
