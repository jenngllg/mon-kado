using Microsoft.AspNetCore.Http;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

/// <summary>
/// Simulates a server request body that rejects reads after exceeding its configured limit.
/// </summary>
public class PayloadTooLargeReadStream : Stream
{
    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {

        throw new BadHttpRequestException(
            "The request body is too large.",
            StatusCodes.Status413PayloadTooLarge);
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {

        throw new BadHttpRequestException(
            "The request body is too large.",
            StatusCodes.Status413PayloadTooLarge);
    }

    /// <inheritdoc />
    public override long Seek(
        long offset,
        SeekOrigin origin)
    {

        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {

        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {

        throw new NotSupportedException();
    }
}
