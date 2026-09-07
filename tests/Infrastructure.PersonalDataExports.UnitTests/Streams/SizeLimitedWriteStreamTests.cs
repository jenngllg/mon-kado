using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Streams;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Streams;

public class SizeLimitedWriteStreamTests
{
    [Fact]
    public async Task WriteAsync_WhenDifferentOverloadsAreUsed_CountsAllBytesAndLeavesDestinationOwnedByCaller()
    {
        // Arrange
        using var destination = new MemoryStream();
        var stream = new SizeLimitedWriteStream(
            destination,
            6);

        // Act
        stream.WriteByte(1);
        stream.Write(
            [2],
            0,
            1);
        stream.Write(new ReadOnlySpan<byte>([3]));
#pragma warning disable CA1835 // Verify the legacy Stream overload remains bounded too.

        await stream.WriteAsync(
            new byte[] { 4 },
            0,
            1,
            TestContext.Current.CancellationToken);
#pragma warning restore CA1835
        await stream.WriteAsync(
            new ReadOnlyMemory<byte>([
                    5,
                    6
                ]),
            TestContext.Current.CancellationToken);
        stream.Flush();
        await stream.FlushAsync(TestContext.Current.CancellationToken);
        await stream.DisposeAsync();

        // Assert
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.True(stream.CanWrite);
        Assert.Equal(
            6,
            stream.Length);
        Assert.Equal(
            6,
            stream.Position);
        Assert.Equal(
            6,
            stream.BytesWritten);
        Assert.Equal(
            new byte[] {
                1,
                2,
                3,
                4,
                5,
                6
            },
            destination.ToArray());
        Assert.True(destination.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.Seek(
                0,
                SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.Read(
                new byte[1],
                0,
                1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_WhenLimitWouldBeExceeded_PreservesPreviouslyWrittenBytes(bool asynchronous)
    {
        // Arrange
        using var destination = new MemoryStream();
        await using var stream = new SizeLimitedWriteStream(
            destination,
            1);
        stream.WriteByte(1);

        // Act
        if (asynchronous)
            await Assert.ThrowsAsync<PersonalDataExportTooLargeException>(() => stream
                    .WriteAsync(
                    new byte[] { 2 },
                    TestContext.Current.CancellationToken)
                    .AsTask());
        else
            Assert.Throws<PersonalDataExportTooLargeException>(() => stream.WriteByte(2));

        // Assert
        Assert.Equal(
            1,
            stream.BytesWritten);
        Assert.Equal(
            new byte[] { 1 },
            destination.ToArray());
    }

    [Fact]
    public async Task WriteAsync_WhenCancelled_DoesNotWriteToDestination()
    {
        // Arrange
        using var destination = new MemoryStream();
        await using var stream = new SizeLimitedWriteStream(
            destination,
            1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream
                .WriteAsync(
                new byte[] { 1 },
                cancellation.Token)
                .AsTask());

        // Assert
        Assert.Equal(
            0,
            destination.Length);
        Assert.Equal(
            0,
            stream.BytesWritten);
    }

    [Fact]
    public async Task WriteAsync_WhenDestinationIsExternal_ForwardsTheExactCancellationToken()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var content = new ReadOnlyMemory<byte>([1]);
        var destinationMock = new Mock<Stream>(MockBehavior.Strict);
        destinationMock
            .Setup(destination => destination.WriteAsync(
                content,
                token))
            .Returns(ValueTask.CompletedTask);
        destinationMock
            .Setup(destination => destination.FlushAsync(token))
            .Returns(Task.CompletedTask);
        await using var stream = new SizeLimitedWriteStream(
            destinationMock.Object,
            1);

        // Act
        await stream.WriteAsync(
            content,
            token);
        await stream.FlushAsync(token);

        // Assert
        Assert.Equal(
            1,
            stream.BytesWritten);
        destinationMock.Verify(
            destination => destination.WriteAsync(
                content,
                token),
            Times.Once);
        destinationMock.Verify(
            destination => destination.FlushAsync(token),
            Times.Once);
        destinationMock.VerifyNoOtherCalls();
    }
}
