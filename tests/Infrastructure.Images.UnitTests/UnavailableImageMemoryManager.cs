using System.Buffers;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests;

/// <summary>Represents image memory that can no longer be accessed by its consumer.</summary>
public class UnavailableImageMemoryManager : MemoryManager<byte>
{
    /// <inheritdoc />
    public override Memory<byte> Memory => CreateMemory(1);

    /// <inheritdoc />
    public override Span<byte> GetSpan()
    {
        throw new InvalidOperationException("The image memory is unavailable.");
    }

    /// <inheritdoc />
    public override MemoryHandle Pin(int elementIndex = 0)
    {
        throw new InvalidOperationException("The image memory is unavailable.");
    }

    /// <inheritdoc />
    public override void Unpin()
    {
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
    }
}
