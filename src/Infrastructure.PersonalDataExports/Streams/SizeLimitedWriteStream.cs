using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Streams;

/// <summary>Bounds every archive byte, including its final ZIP directory, while leaving the caller-owned destination open.</summary>
/// <param name="destination">The destination whose lifetime remains owned by the caller.</param>
/// <param name="maximumBytes">The permitted complete archive length.</param>
public class SizeLimitedWriteStream(
    Stream destination,
    long maximumBytes) : Stream
{
    private long _bytesWritten;
    /// <summary>Gets the successfully written archive length.</summary>
    public long BytesWritten => _bytesWritten;
    /// <inheritdoc/>
    public override bool CanRead => false;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => destination.CanWrite;
    /// <inheritdoc/>
    public override long Length => _bytesWritten;
    /// <inheritdoc/>
    public override long Position
    {
        get => _bytesWritten; set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        Write(buffer.AsSpan(
                offset,
                count));
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        destination.Write(buffer);
        _bytesWritten += buffer.Length;
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value;
        Write(buffer);
    }

    /// <inheritdoc/>
    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {

        return WriteAsync(
            buffer.AsMemory(
                offset,
                count),
            cancellationToken)
            .AsTask();
    }

    /// <inheritdoc/>
    [SuppressMessage("CodeQuality", "S1006:Method overrides should not change parameter defaults", Justification = "MonKado requires an explicit CancellationToken for asynchronous I/O; calls through Stream retain the framework default.")]
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCapacity(buffer.Length);
        await destination.WriteAsync(
            buffer,
            cancellationToken);
        _bytesWritten += buffer.Length;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        destination.Flush();
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {

        return destination.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {

        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override long Seek(
        long offset,
        SeekOrigin origin)
    {

        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {

        throw new NotSupportedException();
    }

    /// <summary>Rejects writes before any byte beyond the limit reaches storage.</summary>
    /// <param name="count">The pending write length.</param>
    /// <exception cref="PersonalDataExportTooLargeException">The complete archive would exceed its limit.</exception>
    private void EnsureCapacity(int count)
    {

        if (count > maximumBytes - _bytesWritten)
            throw new PersonalDataExportTooLargeException();
    }
}
