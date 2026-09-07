using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Services;

public class LocalPersonalDataExportStoreTests : IDisposable
{
    private readonly string _storagePath;
    private readonly LocalPersonalDataExportStore _store;
    public LocalPersonalDataExportStoreTests()
    {
        _storagePath = Path.Combine(
            Path.GetTempPath(),
            $"monkado-export-tests-{Guid.CreateVersion7():N}");
        _store = CreateStore();
    }

    [Theory]
    [InlineData("notes.txt", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.unknown", false)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.tmp", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.tmp", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.manifest.tmp", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip", true)]
    public async Task DeleteAsync_WhenDirectoryContainsKnownAndForeignNames_DeletesOnlyGeneratedFilePatterns(
        string name,
        bool removed)
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var directory = Path.Combine(
            _storagePath,
            exportId.ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            name);
        await File.WriteAllTextAsync(
            path,
            "content",
            TestContext.Current.CancellationToken);

        // Act
        var cleaned = await _store.DeleteAsync(
            exportId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(cleaned);
        Assert.Equal(
            !removed,
            File.Exists(path));
    }

    [Fact]
    public async Task WriteAsync_WhenSuccessful_PublishesOnlyTheCompletedImmutableFile()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        var content = new byte[]
        {
            1,
            2,
            3
        };

        // Act
        var length = await _store.WriteAsync(
            exportId,
            archiveId,
            async (
                context,
                cancellationToken) =>
            {
                Assert.Equal(
                    TestContext.Current.CancellationToken,
                    cancellationToken);
                Assert.True(context.ImageManifest.CanSeek);
                await context.Archive.WriteAsync(
                    content,
                    cancellationToken);
            },
            100,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            3,
            length);
        await using var stream = await _store.OpenReadAsync(
            exportId,
            archiveId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        var actual = new byte[3];
        await stream.ReadExactlyAsync(
            actual,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            content,
            actual);
        var files = Directory.GetFiles(GetDirectory(exportId));
        Assert.Equal(
            archiveId.ToString("N") + ".zip",
            Path.GetFileName(Assert.Single(files)));
    }

    [Fact]
    public async Task WriteAsync_WhenSizeLimitExceeded_DoesNotPublishAPartialArchive()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();

        // Act
        var action = () => _store.WriteAsync(
            exportId,
            archiveId,
            async (
                context,
                cancellationToken) => await context.Archive.WriteAsync(
                new byte[10],
                cancellationToken),
            5,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PersonalDataExportTooLargeException>(action);
        Assert.Null(await _store.OpenReadAsync(
                exportId,
                archiveId,
                TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(
                GetDirectory(exportId),
                "*.zip"));
    }

    [Fact]
    public async Task DeleteAsync_WhenAnotherInstanceIsWriting_PreservesTheActiveAttempt()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var other = CreateStore();
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = _store.WriteAsync(
            exportId,
            archiveId,
            async (
                context,
                cancellationToken) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                await context.Archive.WriteAsync(
                    new byte[] { 1 },
                    cancellationToken);
            },
            100,
            TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        bool deleted;
        try
        {

            // Act
            deleted = await other.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken);
            await other.ReconcileAsync(
                DateTime.MaxValue,
                100,
                (
                    _,
                    _,
                    _) => Task.FromResult(false),
                TestContext.Current.CancellationToken);

            // Assert
            Assert.False(deleted);
            Assert.Null(await other.OpenReadAsync(
                    exportId,
                    archiveId,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult();
        }

        await writing;
        Assert.True(await other.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken));
        Assert.True(await other.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(GetDirectory(exportId)));
    }

    [Fact]
    public async Task ReconcileAsync_WhenAttemptsAreOld_PreservesReferencedAndForeignFiles()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        await WriteSmallArchiveAsync(
            exportId,
            archiveId);
        var directory = GetDirectory(exportId);
        var abandoned = Guid.CreateVersion7();
        var abandonedPath = Path.Combine(
            directory,
            abandoned.ToString("N") + ".tmp");
        var foreignPath = Path.Combine(
            directory,
            "user-document.txt");
        await File.WriteAllTextAsync(
            abandonedPath,
            "interrupted",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            foreignPath,
            "foreign",
            TestContext.Current.CancellationToken);
        var checkedIds = new List<Guid>();

        // Act
        await _store.ReconcileAsync(
            DateTime.MaxValue,
            100,
            (
                checkedExport,
                checkedArchive,
                cancellationToken) =>
            {
                Assert.Equal(
                    exportId,
                    checkedExport);
                Assert.Equal(
                    TestContext.Current.CancellationToken,
                    cancellationToken);
                checkedIds.Add(checkedArchive);

                return Task.FromResult(checkedArchive == archiveId);
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            archiveId,
            checkedIds);
        Assert.Contains(
            abandoned,
            checkedIds);
        Assert.False(File.Exists(abandonedPath));
        Assert.True(File.Exists(foreignPath));
        await using var stream = await _store.OpenReadAsync(
            exportId,
            archiveId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task ReconcileAsync_WhenFilesAreRecent_DoesNotCheckOrDeleteThem()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        await WriteSmallArchiveAsync(
            exportId,
            archiveId);

        // Act
        await _store.ReconcileAsync(
            DateTime.MinValue,
            100,
            (
                _,
                _,
                _) => throw new InvalidOperationException("Recent files must not be examined."),
            TestContext.Current.CancellationToken);

        // Assert
        await using var stream = await _store.OpenReadAsync(
            exportId,
            archiveId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task OpenReadAsync_WhenVolumeIsMissing_ReturnsSanitizedStorageFailure()
    {
        // Arrange
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();

        // Act
        var action = () => _store.OpenReadAsync(
            exportId,
            archiveId,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(action);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            _storagePath,
            exception.Message);
        Assert.False(Directory.Exists(_storagePath));
    }

    [Fact]
    public async Task DeleteAsync_WhenForeignFileExists_LeavesItUntouched()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        await WriteSmallArchiveAsync(
            exportId,
            archiveId);
        var foreignPath = Path.Combine(
            GetDirectory(exportId),
            "keep.txt");
        await File.WriteAllTextAsync(
            foreignPath,
            "foreign",
            TestContext.Current.CancellationToken);

        // Act
        var deleted = await _store.DeleteAsync(
            exportId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deleted);
        Assert.True(File.Exists(foreignPath));
        Assert.Null(await _store.OpenReadAsync(
                exportId,
                archiveId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReconcileAsync_WhenFirstBatchIsProtected_AdvancesToLaterOrphans()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var protectedId = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var orphanId = Guid.Parse("019a0000-0000-7000-8000-000000000002");
        await WriteSmallArchiveAsync(
            exportId,
            protectedId);
        await WriteSmallArchiveAsync(
            exportId,
            orphanId);
        var checkedIds = new List<Guid>();
        Task<bool> IsReferencedAsync(
            Guid checkedExport,
            Guid checkedArchive,
            CancellationToken cancellationToken)
        {
            Assert.Equal(
                TestContext.Current.CancellationToken,
                cancellationToken);
            Assert.Equal(
                exportId,
                checkedExport);
            checkedIds.Add(checkedArchive);

            return Task.FromResult(checkedArchive == protectedId);
        }

        // Act
        await _store.ReconcileAsync(
            DateTime.MaxValue,
            1,
            IsReferencedAsync,
            TestContext.Current.CancellationToken);
        await _store.ReconcileAsync(
            DateTime.MaxValue,
            1,
            IsReferencedAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new[] {
                protectedId,
                orphanId
            },
            checkedIds);
        await using var preserved = await _store.OpenReadAsync(
            exportId,
            protectedId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(preserved);
        Assert.Null(await _store.OpenReadAsync(
                exportId,
                orphanId,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconcileAsync_WhenDirectoryHasNoAttempt_RemovesOnlyOldEmptyDirectories(bool old)
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var directory = GetDirectory(Guid.CreateVersion7());
        Directory.CreateDirectory(directory);

        // Act
        await _store.ReconcileAsync(
            old ? DateTime.MaxValue : DateTime.MinValue,
            1,
            (
                _,
                _,
                _) => throw new InvalidOperationException("An empty directory has no image reference."),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            !old,
            Directory.Exists(directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_WhenCallbackFailsWithStorageError_SanitizesFailure(bool unauthorized)
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var failure = unauthorized ? (Exception)new UnauthorizedAccessException("private-path") : new IOException("private-path");

        // Act
        var exception = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _store.WriteAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                (
                    _,
                    _) => Task.FromException(failure),
                100,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            "private-path",
            exception.ToString());
    }

    [Fact]
    public async Task InitializeAsync_WhenRootIsAFile_ReportsUnavailableWithoutExposingItsPath()
    {
        // Arrange
        await File.WriteAllTextAsync(
            _storagePath,
            "foreign",
            TestContext.Current.CancellationToken);
        try
        {

            // Act
            var failure = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _store.InitializeAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(
                _storagePath,
                failure.ToString());
            Assert.Equal(
                "foreign",
                await File.ReadAllTextAsync(
                    _storagePath,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(_storagePath);
        }
    }

    [Fact]
    public async Task WriteAsync_WhenAnotherInstanceOwnsTheStripe_RejectsSecondWriterAndDefersCleanup()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        var second = CreateStore();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = _store.WriteAsync(
            exportId,
            archiveId,
            async (
                context,
                token) =>
            {
                started.SetResult();
                await finish.Task.WaitAsync(token);
                await context.Archive.WriteAsync(
                    new byte[] { 1 },
                    token);
            },
            100,
            TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {

            // Act
            await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => second.WriteAsync(
                    exportId,
                    Guid.CreateVersion7(),
                    (
                        _,
                        _) => Task.CompletedTask,
                    100,
                    TestContext.Current.CancellationToken));
            var deleted = await second.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken);
            await second.ReconcileAsync(
                DateTime.MaxValue,
                100,
                (
                    _,
                    _,
                    _) => throw new InvalidOperationException("An active writer must not be reconciled."),
                TestContext.Current.CancellationToken);

            // Assert
            Assert.False(deleted);
        }
        finally
        {
            finish.TrySetResult();
            await writing;
        }

        Assert.True(await second.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconcileAsync_WhenReferenceReadFails_ResetsCursorAndResumesWithoutLeakingPaths(bool unauthorized)
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        await WriteSmallArchiveAsync(
            exportId,
            archiveId);
        var exception = unauthorized ? (Exception)new UnauthorizedAccessException("private-path") : new IOException("private-path");

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _store.ReconcileAsync(
                DateTime.MaxValue,
                100,
                (
                    _,
                    _,
                    _) => Task.FromException<bool>(exception),
                TestContext.Current.CancellationToken));
        await _store.ReconcileAsync(
            DateTime.MaxValue,
            100,
            (
                _,
                _,
                _) => Task.FromResult(false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(
            "private-path",
            failure.ToString());
        Assert.Null(await _store.OpenReadAsync(
                exportId,
                archiveId,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("missing-directory")]
    [InlineData("missing-file")]
    [InlineData("locked-file")]
    [InlineData("directory")]
    public async Task OpenReadAsync_WhenFileCannotBeOpened_DistinguishesAbsenceFromInvalidStorage(string scenario)
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        var path = Path.Combine(
            GetDirectory(exportId),
            $"{archiveId:N}.zip");

        if (scenario != "missing-directory")
            Directory.CreateDirectory(GetDirectory(exportId));

        if (scenario == "directory")
            Directory.CreateDirectory(path);
        await using var lease = scenario == "locked-file" ? new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None) : null;

        // Act
        if (scenario.StartsWith(
            "missing",
            StringComparison.Ordinal))
            Assert.Null(await _store.OpenReadAsync(
                    exportId,
                    archiveId,
                    TestContext.Current.CancellationToken));
        else
        {
            var failure = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _store.OpenReadAsync(
                    exportId,
                    archiveId,
                    TestContext.Current.CancellationToken));

            // Assert
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(
                _storagePath,
                failure.ToString());
        }
    }

    [Fact(Skip = "Read-only file deletion is enforced by Windows; Unix deletion uses directory permissions.", SkipUnless = nameof(IsWindows))]
    public async Task DeleteAsync_WhenArchiveIsReadOnly_ReturnsSanitizedFailureThenRecovers()
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        await WriteSmallArchiveAsync(
            exportId,
            archiveId);
        var path = Path.Combine(
            GetDirectory(exportId),
            $"{archiveId:N}.zip");
        File.SetAttributes(
            path,
            FileAttributes.ReadOnly);
        try
        {

            // Act
            var failure = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _store.DeleteAsync(
                    exportId,
                    TestContext.Current.CancellationToken));

            // Assert
            Assert.Null(failure.InnerException);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.SetAttributes(
                path,
                FileAttributes.Normal);
        }

        Assert.True(await _store.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Theory(Skip = "Symbolic links require an unavailable Windows privilege; verified on Linux.", SkipWhen = nameof(IsWindows))]
    [InlineData("root")]
    [InlineData("directory")]
    [InlineData("archive")]
    [InlineData("lock")]
    public async Task Store_WhenGeneratedPathIsSymlink_RejectsOrPreservesForeignTarget(string kind)
    {
        // Arrange
        await _store.InitializeAsync(TestContext.Current.CancellationToken);
        var exportId = Guid.CreateVersion7();
        var archiveId = Guid.CreateVersion7();
        var target = Path.Combine(
            _storagePath,
            "foreign-target");
        Directory.CreateDirectory(target);
        var path = GetDirectory(exportId);

        if (kind == "directory")
            Directory.CreateSymbolicLink(
                path,
                target);
        else
            if (kind == "root")
            {
                var link = Path.Combine(
                    _storagePath,
                    "root-link");
                Directory.CreateSymbolicLink(
                    link,
                    target);
                var linkedStore = new LocalPersonalDataExportStore(
                    Microsoft.Extensions.Options.Options.Create(new PersonalDataExportStorageOptions { StoragePath = link }),
                    TimeProvider.System);

                // Act
                await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => linkedStore.InitializeAsync(TestContext.Current.CancellationToken));
            }
            else
            {
                Directory.CreateDirectory(path);
                path = kind == "archive" ? Path.Combine(
                    path,
                    $"{archiveId:N}.zip") : Path.Combine(
                    _storagePath,
                    ".locks",
                    $"{exportId.ToByteArray()[15] & 63:D2}.lock");
                File.CreateSymbolicLink(
                    path,
                    Path.Combine(
                        target,
                        "missing-foreign-file"));
            }

        if (kind == "archive")
        {
            await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _store.OpenReadAsync(
                    exportId,
                    archiveId,
                    TestContext.Current.CancellationToken));
            await _store.DeleteAsync(
                exportId,
                TestContext.Current.CancellationToken);
        }

        if (kind is "directory" or "lock")
            await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => WriteSmallArchiveAsync(
                    exportId,
                    archiveId));

        // Assert
        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    public void Dispose()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var resolvedPath = Path.GetFullPath(_storagePath);

        if (!resolvedPath.StartsWith(
            temporaryRoot,
            StringComparison.OrdinalIgnoreCase) || !Path
            .GetFileName(resolvedPath)
            .StartsWith(
            "monkado-export-tests-",
            StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a directory outside this test fixture.");

        if (Directory.Exists(resolvedPath))
            Directory.Delete(
                resolvedPath,
                recursive: true);
        GC.SuppressFinalize(this);
    }

    private LocalPersonalDataExportStore CreateStore()
    {

        return new LocalPersonalDataExportStore(
            Microsoft.Extensions.Options.Options.Create(new PersonalDataExportStorageOptions { StoragePath = _storagePath }),
            TimeProvider.System);
    }

    private Task<long> WriteSmallArchiveAsync(
        Guid exportId,
        Guid archiveId)
    {

        return _store.WriteAsync(
            exportId,
            archiveId,
            async (
                context,
                cancellationToken) => await context.Archive.WriteAsync(
                new byte[] { 1 },
                cancellationToken),
            100,
            TestContext.Current.CancellationToken);
    }

    private string GetDirectory(Guid exportId)
    {

        return Path.Combine(
            _storagePath,
            exportId.ToString("N"));
    }
}
