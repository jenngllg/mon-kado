using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

public class LocalGiftImageStoreTests : IDisposable
{
    public static bool RequiresWindowsSymbolicLinkPrivilege => OperatingSystem.IsWindows();

    [Fact]
    public async Task WritePendingAsync_WhenSourceMemoryIsUnavailable_DoesNotReportStorageFailure()
    {
        // Arrange
        using var memory = new UnavailableImageMemoryManager();

        // Act
        var action = () => _store.WritePendingAsync(
            Guid.CreateVersion7(),
            memory.Memory,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Empty(Directory.EnumerateFiles(
                _storagePath,
                "*.tmp",
                SearchOption.AllDirectories));
    }

    private readonly string _storagePath;
    private readonly LocalGiftImageStore _store;

    [Fact]
    public async Task WritePendingAsync_WhenStoragePathHasTrailingSeparator_WritesAndCleansSafely()
    {
        // Arrange
        var store = new LocalGiftImageStore(
            Microsoft.Extensions.Options.Options.Create(new GiftImageStorageOptions
            {
                StoragePath = _storagePath + Path.DirectorySeparatorChar
            }),
            TimeProvider.System);
        var imageId = Guid.CreateVersion7();

        // Act
        await store.WritePendingAsync(
            imageId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await store.CleanupTemporaryAsync(
            DateTime.MaxValue,
            10,
            TestContext.Current.CancellationToken);

        // Assert
        await using var stream = await store.OpenReadAsync(
            imageId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.Equal(
            1,
            stream.ReadByte());
    }

    public LocalGiftImageStoreTests()
    {
        _storagePath = Path.Combine(
            Path.GetTempPath(),
            $"mon-kado-images-{Guid.NewGuid():N}");
        _store = new LocalGiftImageStore(
            Microsoft.Extensions.Options.Options.Create(new GiftImageStorageOptions { StoragePath = _storagePath }),
            TimeProvider.System);
    }

    [Fact]
    public async Task WritePendingAsync_WhenImageIsNew_WritesImageAndMarkerUsingIdentifierPath()
    {
        // Arrange
        var imageId = Guid.Parse("019cba55-f3d7-7000-8000-000000000001");
        byte[] content = [
            1,
            2,
            3
        ];

        // Act
        await _store.WritePendingAsync(
            imageId,
            content,
            TestContext.Current.CancellationToken);

        // Assert
        var files = Directory
            .EnumerateFiles(
            _storagePath,
            "*",
            SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(
                ".lock",
                StringComparison.Ordinal))
            .OrderBy(path => path)
            .ToArray();
        Assert.Equal(
            2,
            files.Length);
        Assert.Contains(
            files,
            path => path.EndsWith(
                "019cba55f3d770008000000000000001.webp",
                StringComparison.Ordinal));
        Assert.Contains(
            files,
            path => path.EndsWith(
                "019cba55f3d770008000000000000001.pending",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            files,
            path => path.EndsWith(
                ".tmp",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenReadAsync_WhenImageExists_ReturnsStoredContent()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        byte[] content = [
            4,
            5,
            6
        ];
        await _store.WritePendingAsync(
            imageId,
            content,
            TestContext.Current.CancellationToken);

        // Act
        await using var stream = await _store.OpenReadAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(stream);
        using var destination = new MemoryStream();
        await stream.CopyToAsync(
            destination,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            content,
            destination.ToArray());
    }

    [Fact]
    public async Task OpenReadAsync_WhenImageDoesNotExist_ReturnsNull()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();

        // Act
        var stream = await _store.OpenReadAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(stream);
    }

    [Fact]
    public async Task OpenReadAsync_WhenImageDirectoryExistsButFileDoesNot_ReturnsNull()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        Directory.CreateDirectory(Path.GetDirectoryName(GetImagePath(imageId)) ?? _storagePath);

        // Act
        var stream = await _store.OpenReadAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(stream);
    }

    [Fact]
    public async Task OpenReadAsync_WhenImagePathIsADirectory_ThrowsStorageUnavailable()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        Directory.CreateDirectory(GetImagePath(imageId));

        // Act
        var exception = await Record.ExceptionAsync(() => _store.OpenReadAsync(
                imageId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageStorageUnavailableException>(exception);
    }

    [Fact]
    public async Task MarkCommittedAsync_WhenPendingMarkerExists_RemovesOnlyMarker()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        await _store.WritePendingAsync(
            imageId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        // Act
        await _store.MarkCommittedAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        var files = Directory
            .EnumerateFiles(
            _storagePath,
            "*",
            SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(
                ".lock",
                StringComparison.Ordinal))
            .ToArray();
        var imagePath = Assert.Single(files);
        Assert.EndsWith(
            ".webp",
            imagePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkCommittedAsync_WhenPendingMarkerDoesNotExist_Completes()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();

        // Act
        var exception = await Record.ExceptionAsync(() => _store.MarkCommittedAsync(
                imageId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task MarkCommittedAsync_WhenPendingMarkerCannotBeDeleted_ThrowsStorageUnavailable()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        Directory.CreateDirectory(GetPendingPath(imageId));

        // Act
        var exception = await Record.ExceptionAsync(() => _store.MarkCommittedAsync(
                imageId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageStorageUnavailableException>(exception);
    }

    [Fact]
    public async Task DeleteAsync_WhenImageAndMarkerExist_RemovesBoth()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        await _store.WritePendingAsync(
            imageId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        // Act
        await _store.DeleteAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            Directory.EnumerateFiles(
                _storagePath,
                "*",
                SearchOption.AllDirectories),
            path => !path.EndsWith(
                ".lock",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_WhenImageCannotBeDeleted_ThrowsStorageUnavailable()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        Directory.CreateDirectory(GetImagePath(imageId));

        // Act
        var exception = await Record.ExceptionAsync(() => _store.DeleteAsync(
                imageId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageStorageUnavailableException>(exception);
    }

    [Fact]
    public async Task DeleteAsync_WhenVolumeIsMissing_ThrowsStorageUnavailable()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();

        // Act
        var action = () => _store.DeleteAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GiftImageStorageUnavailableException>(action);
        Assert.False(Directory.Exists(_storagePath));
    }

    [Fact]
    public async Task DeleteAsync_WhenVolumeExistsAndShardIsMissing_IsIdempotent()
    {
        // Arrange
        Directory.CreateDirectory(_storagePath);
        var imageId = Guid.CreateVersion7();

        // Act
        await _store.DeleteAsync(
            imageId,
            TestContext.Current.CancellationToken);
        await _store.DeleteAsync(
            imageId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Directory.EnumerateFileSystemEntries(_storagePath));
    }

    [Fact]
    public async Task GetPendingAsync_WhenMarkersHaveDifferentAges_ReturnsOldestEligibleBatch()
    {
        // Arrange
        var oldestId = Guid.CreateVersion7();
        var eligibleId = Guid.CreateVersion7();
        var recentId = Guid.CreateVersion7();
        await _store.WritePendingAsync(
            oldestId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await _store.WritePendingAsync(
            eligibleId,
            new byte[] { 2 },
            TestContext.Current.CancellationToken);
        await _store.WritePendingAsync(
            recentId,
            new byte[] { 3 },
            TestContext.Current.CancellationToken);
        var now = DateTime.UtcNow;
        SetPendingCreationTime(
            oldestId,
            now.AddHours(-3));
        SetPendingCreationTime(
            eligibleId,
            now.AddHours(-2));
        SetPendingCreationTime(
            recentId,
            now);
        var invalidPath = Path.Combine(
            _storagePath,
            "invalid.pending");
        await File.WriteAllBytesAsync(
            invalidPath,
            [],
            TestContext.Current.CancellationToken);

        // Act
        var result = await _store.GetPendingAsync(
            now.AddHours(-1),
            1,
            TestContext.Current.CancellationToken);

        // Assert
        var pending = Assert.Single(result);
        Assert.Equal(
            oldestId,
            pending.ImageId);
    }

    [Fact]
    public async Task GetPendingAsync_WhenStorageDoesNotExist_ReturnsEmptyCollection()
    {
        // Arrange
        var cutoff = DateTime.UtcNow;

        // Act
        var result = await _store.GetPendingAsync(
            cutoff,
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingAsync_WhenStoragePathIsAFile_ThrowsStorageUnavailable()
    {
        // Arrange
        var filePath = _storagePath + ".file";
        await File.WriteAllBytesAsync(
            filePath,
            [],
            TestContext.Current.CancellationToken);
        var store = new LocalGiftImageStore(
            Microsoft.Extensions.Options.Options.Create(new GiftImageStorageOptions { StoragePath = filePath }),
            TimeProvider.System);
        try
        {

            // Act
            var exception = await Record.ExceptionAsync(() => store.GetPendingAsync(
                    DateTime.UtcNow,
                    10,
                    TestContext.Current.CancellationToken));

            // Assert
            Assert.IsType<GiftImageStorageUnavailableException>(exception);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GetPendingAsync_WhenBatchSizeIsInvalid_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var cutoff = DateTime.UtcNow;

        // Act
        var exception = await Record.ExceptionAsync(() => _store.GetPendingAsync(
                cutoff,
                0,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public async Task WritePendingAsync_WhenImageAlreadyExists_ThrowsStorageUnavailable()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        await _store.WritePendingAsync(
            imageId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        // Act
        var exception = await Record.ExceptionAsync(() => _store.WritePendingAsync(
                imageId,
                new byte[] { 2 },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageStorageUnavailableException>(exception);
        Assert.DoesNotContain(
            Directory
                .EnumerateFiles(
                _storagePath,
                "*",
                SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(
                    ".lock",
                    StringComparison.Ordinal)),
            path => path.EndsWith(
                ".tmp",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_WhenStoragePathIsMissing_ThrowsArgumentException(string? storagePath)
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new GiftImageStorageOptions { StoragePath = storagePath });

        // Act
        var exception = Record.Exception(() => new LocalGiftImageStore(
                options,
                TimeProvider.System));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public async Task WritePendingAsync_WhenCancellationIsRequested_DoesNotWrapCancellation()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => _store.WritePendingAsync(
                Guid.CreateVersion7(),
                new byte[] { 1 },
                cancellationTokenSource.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(Directory.EnumerateFiles(
                _storagePath,
                "*.tmp",
                SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DeleteAsync_WhenCancellationIsRequested_ThrowsOperationCanceled()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => _store.DeleteAsync(
                Guid.CreateVersion7(),
                cancellationTokenSource.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task CleanupTemporaryAsync_WhenFilesAreAbandoned_DeletesOnlyRecognizedOldBatch()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        var old = new DateTime(
            2026,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var imagePath = GetImagePath(imageId);
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        var first = imagePath + $".{Guid.NewGuid():N}.tmp";
        var second = GetPendingPath(imageId) + $".{Guid.NewGuid():N}.tmp";
        var recent = imagePath + $".{Guid.NewGuid():N}.tmp";
        foreach (var path in new[]
        {
            first,
            second,
            recent
        }

        )
            await File.WriteAllTextAsync(
                path,
                "partial",
                TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(
            first,
            old);
        File.SetLastWriteTimeUtc(
            second,
            old);
        File.SetLastWriteTimeUtc(
            recent,
            old.AddHours(2));
        await File.WriteAllTextAsync(
            imagePath,
            "committed",
            TestContext.Current.CancellationToken);

        // Act
        await _store.CleanupTemporaryAsync(
            old.AddHours(1),
            1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(
            new[] {
                first,
                second
            },
            File.Exists);
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(imagePath));

        // Act
        await _store.CleanupTemporaryAsync(
            old.AddHours(1),
            10,
            TestContext.Current.CancellationToken);
        await _store.CleanupTemporaryAsync(
            old.AddHours(1),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(recent));
    }

    [Theory]
    [InlineData("foreign.tmp")]
    [InlineData("bad.webp.00000000000000000000000000000000.tmp")]
    [InlineData("00000000000000000000000000000000.other.00000000000000000000000000000000.tmp")]
    [InlineData("00000000000000000000000000000000.webp.bad.tmp")]
    [InlineData("00000000000000000000000000000000.webp.00000000000000000000000000000000.tmp")]
    public async Task CleanupTemporaryAsync_WhenFileIsForeignOrMisplaced_PreservesIt(string name)
    {
        // Arrange
        Directory.CreateDirectory(_storagePath);
        var path = Path.Combine(
            _storagePath,
            name);
        await File.WriteAllTextAsync(
            path,
            "foreign",
            TestContext.Current.CancellationToken);
        var cutoff = new DateTime(
            2026,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(
            path,
            cutoff);

        // Act
        await _store.CleanupTemporaryAsync(
            cutoff,
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task CleanupTemporaryAsync_WhenWriterOwnsPartition_SkipsUntilLeaseIsReleased()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        await _store.WritePendingAsync(
            imageId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        var temporary = GetImagePath(imageId) + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporary,
            "partial",
            TestContext.Current.CancellationToken);
        var cutoff = new DateTime(
            2026,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(
            temporary,
            cutoff);
        var lockPath = Path.Combine(
            _storagePath,
            ".cleanup-locks",
            imageId.ToString("N")[^2..] + ".lock");
        using (var lease = new FileStream(
            lockPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {

            // Act
            await _store.CleanupTemporaryAsync(
                cutoff,
                10,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(File.Exists(temporary));
        }

        // Act
        await Task.WhenAll(
            Task.Run(
                () => _store.CleanupTemporaryAsync(
                    cutoff,
                    10,
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken),
            Task.Run(
                () => _store.CleanupTemporaryAsync(
                    cutoff,
                    10,
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.False(File.Exists(temporary));
        Assert.True(File.Exists(GetImagePath(imageId)));
    }

    [Fact]
    public async Task CleanupTemporaryAsync_WhenStorageIsAbsent_Completes()
    {
        // Arrange
        var cutoff = new DateTime(
            2026,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        await _store.CleanupTemporaryAsync(
            cutoff,
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Directory.Exists(_storagePath));
    }

    [Fact]
    public async Task CleanupTemporaryAsync_WhenStorageIsUnavailable_ReportsSafeFailure()
    {
        // Arrange
        await File.WriteAllTextAsync(
            _storagePath,
            "not a directory",
            TestContext.Current.CancellationToken);
        try
        {

            // Act
            var failure = await Record.ExceptionAsync(() => _store.CleanupTemporaryAsync(
                    DateTime.MaxValue,
                    10,
                    TestContext.Current.CancellationToken));

            // Assert
            Assert.IsType<GiftImageStorageUnavailableException>(failure);
        }
        finally
        {
            File.Delete(_storagePath);
        }
    }

    [Fact(Skip = "Symbolic-link security is verified on Linux; Windows requires an unavailable OS privilege.", SkipWhen = nameof(RequiresWindowsSymbolicLinkPrivilege))]
    public async Task WritePendingAsync_WhenLockIsSymbolicLink_RejectsIt()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        var lockDirectory = Path.Combine(
            _storagePath,
            ".cleanup-locks");
        Directory.CreateDirectory(lockDirectory);
        var target = Path.Combine(
            _storagePath,
            "unrelated.txt");
        await File.WriteAllTextAsync(
            target,
            "untouched",
            TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(
            Path.Combine(
                lockDirectory,
                imageId.ToString("N")[^2..] + ".lock"),
            target);

        // Act
        var failure = await Record.ExceptionAsync(() => _store.WritePendingAsync(
                imageId,
                new byte[] { 1 },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageStorageUnavailableException>(failure);
        Assert.Equal(
            "untouched",
            await File.ReadAllTextAsync(
                target,
                TestContext.Current.CancellationToken));
    }

    [Fact(Skip = "Symbolic-link security is verified on Linux; Windows requires an unavailable OS privilege.", SkipWhen = nameof(RequiresWindowsSymbolicLinkPrivilege))]
    public async Task CleanupTemporaryAsync_WhenCandidatesAreSymbolicLinks_PreservesTargets()
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        var imagePath = GetImagePath(imageId);
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        var target = Path.Combine(
            _storagePath,
            "target.txt");
        await File.WriteAllTextAsync(
            target,
            "untouched",
            TestContext.Current.CancellationToken);
        var link = imagePath + $".{Guid.NewGuid():N}.tmp";
        File.CreateSymbolicLink(
            link,
            target);

        // Act
        await _store.CleanupTemporaryAsync(
            DateTime.MaxValue,
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(File.Exists(link));
        Assert.Equal(
            "untouched",
            await File.ReadAllTextAsync(
                target,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupTemporaryAsync_WhenCancelled_PropagatesCancellation()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        // Act
        var failure = await Record.ExceptionAsync(() => _store.CleanupTemporaryAsync(
                DateTime.MaxValue,
                10,
                source.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
    }

    [Fact]
    public async Task CleanupTemporaryAsync_WhenBatchIsInvalid_RejectsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var failure = await Record.ExceptionAsync(() => _store.CleanupTemporaryAsync(
                DateTime.MaxValue,
                0,
                cancellationToken));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WritePendingAsync_WhenPartitionIsBusy_WaitsOrCancels(bool cancel)
    {
        // Arrange
        var imageId = Guid.CreateVersion7();
        var lockDirectory = Path.Combine(
            _storagePath,
            ".cleanup-locks");
        Directory.CreateDirectory(lockDirectory);
        using var lease = new FileStream(
            Path.Combine(
                lockDirectory,
                imageId.ToString("N")[^2..] + ".lock"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = new LeaseContentionTimeProvider(() =>
            {

                if (cancel)
                    source.Cancel();
                lease.Dispose();
            });
        var store = new LocalGiftImageStore(
            Microsoft.Extensions.Options.Options.Create(new GiftImageStorageOptions { StoragePath = _storagePath }),
            clock);

        // Act
        var failure = await Record.ExceptionAsync(() => store.WritePendingAsync(
                imageId,
                new byte[] { 1 },
                source.Token));

        // Assert
        Assert.Equal(
            TimeSpan.FromMilliseconds(10),
            clock.Delay);

        if (cancel)
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
        else
        {
            Assert.Null(failure);
            Assert.True(File.Exists(GetImagePath(imageId)));
        }
    }

    public void Dispose()
    {

        if (Directory.Exists(_storagePath))
            Directory.Delete(
                _storagePath,
                recursive: true);
        GC.SuppressFinalize(this);
    }

    private void SetPendingCreationTime(
        Guid imageId,
        DateTime createdAt)
    {
        var fileName = imageId.ToString("N") + ".pending";
        var path = Directory
            .EnumerateFiles(
            _storagePath,
            fileName,
            SearchOption.AllDirectories)
            .Single();
        File.SetLastWriteTimeUtc(
            path,
            createdAt);
    }

    private string GetImagePath(Guid imageId)
    {
        var value = imageId.ToString("N");

        return Path.Combine(
            _storagePath,
            value[..2],
            value.Substring(
                2,
                2),
            value + ".webp");
    }

    private string GetPendingPath(Guid imageId)
    {

        return Path.ChangeExtension(
            GetImagePath(imageId),
            ".pending");
    }
}
