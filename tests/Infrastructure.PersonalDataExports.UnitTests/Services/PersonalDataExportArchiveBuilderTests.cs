using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Services;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Streams;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Moq;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Services;

public class PersonalDataExportArchiveBuilderTests : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Mock<IPersonalDataExportSnapshotReader> _snapshotReaderMock;
    private readonly Mock<IGiftImageStore> _imageStoreMock;
    private readonly Mock<IPersonalDataExportStore> _exportStoreMock;
    private readonly PersonalDataExportArchiveBuilder _builder;
    private readonly PersonalDataExportWorkItem _workItem;
    private readonly MemoryStream _archive = new();
    private readonly MemoryStream _manifest = new();
    private readonly DateTime _snapshotAt = new(
        2026,
        9,
        7,
        12,
        0,
        0,
        DateTimeKind.Utc);
    private readonly PersonalDataExportOptions _options = new();
    public PersonalDataExportArchiveBuilderTests()
    {
        _snapshotReaderMock = new(MockBehavior.Strict);
        _imageStoreMock = new(MockBehavior.Strict);
        _exportStoreMock = new(MockBehavior.Strict);
        _workItem = TestFixture
            .Create()
            .Create<PersonalDataExportWorkItem>();
        _builder = new PersonalDataExportArchiveBuilder(
            _snapshotReaderMock.Object,
            _imageStoreMock.Object,
            _exportStoreMock.Object,
            Microsoft.Extensions.Options.Options.Create(_options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAsync_WhenSnapshotHasVerifiedImage_CreatesCompleteZipWithoutPrivateManifest(bool profileImage)
    {
        // Arrange
        ConfigureStore();
        var content = Encoding.UTF8.GetBytes("immutable WebP bytes");
        var image = new PersonalDataExportImage
        {
            ImageId = Guid.CreateVersion7(),
            WishId = profileImage ? null : Guid.CreateVersion7(),
            ContentHash = SHA256.HashData(content)
        };
        ConfigureSnapshot([image]);
        _imageStoreMock
            .Setup(store => store.OpenReadAsync(
                image.ImageId,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new MemoryStream(content));

        // Act
        var result = await _builder.BuildAsync(
            _workItem,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            _snapshotAt,
            result.SnapshotAt);
        Assert.Equal(
            _archive.Length,
            result.SizeInBytes);
        _archive.Position = 0;
        using var zip = new ZipArchive(
            _archive,
            ZipArchiveMode.Read,
            leaveOpen: true);
        Assert.Equal(
            3,
            zip.Entries.Count);
        var entryName = profileImage ? "images/profile.webp" : $"images/wishes/{image.WishId:D}.webp";
        var entry = zip.GetEntry(entryName);
        Assert.NotNull(entry);
        await using var imageStream = entry.Open();
        var actual = new byte[content.Length];
        await imageStream.ReadExactlyAsync(
            actual,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            content,
            actual);
        var data = zip.GetEntry("data.json");
        Assert.NotNull(data);
        await using var dataStream = data.Open();
        using var document = await JsonDocument.ParseAsync(
            dataStream,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            1,
            document.RootElement
                .GetProperty("schemaVersion")
                .GetInt32());
        Assert.NotNull(zip.GetEntry("README.txt"));
        Assert.DoesNotContain(
            zip.Entries,
            entry => entry.FullName.Contains(
                "manifest",
                StringComparison.OrdinalIgnoreCase));
        _imageStoreMock.Verify(
            store => store.OpenReadAsync(
                image.ImageId,
                TestContext.Current.CancellationToken),
            Times.Once);
        VerifySnapshotAndStore();
    }

    [Fact]
    public async Task BuildAsync_WhenSnapshotHasNoImages_CreatesDataAndReadmeOnly()
    {
        // Arrange
        ConfigureStore();
        ConfigureSnapshot([]);

        // Act
        var result = await _builder.BuildAsync(
            _workItem,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            _snapshotAt,
            result.SnapshotAt);
        _archive.Position = 0;
        using var zip = new ZipArchive(
            _archive,
            ZipArchiveMode.Read,
            leaveOpen: true);
        Assert.Equal(
            2,
            zip.Entries.Count);
        VerifySnapshotAndStore();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAsync_WhenImageMissingOrDigestMismatch_RejectsIncompleteArchive(bool missing)
    {
        // Arrange
        ConfigureStore();
        var image = new PersonalDataExportImage
        {
            ImageId = Guid.CreateVersion7(),
            ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes("expected"))
        };
        ConfigureSnapshot([image]);
        _imageStoreMock
            .Setup(store => store.OpenReadAsync(
                image.ImageId,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(missing ? null : new MemoryStream(Encoding.UTF8.GetBytes("corrupt")));

        // Act
        var exception = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _builder.BuildAsync(
                _workItem,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception.InnerException);
        _imageStoreMock.Verify(
            store => store.OpenReadAsync(
                image.ImageId,
                TestContext.Current.CancellationToken),
            Times.Once);
        VerifySnapshotAndStore();
    }

    [Fact]
    public async Task BuildAsync_WhenManifestContainsNull_RejectsCorruptInternalReference()
    {
        // Arrange
        ConfigureStore();
        ConfigureSnapshot([null]);

        // Act
        await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _builder.BuildAsync(
                _workItem,
                TestContext.Current.CancellationToken));

        // Assert
        VerifySnapshotAndStore();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 1 })]
    public async Task BuildAsync_WhenImageDigestIsMissingOrMalformed_RejectsUnverifiableContent(byte[]? digest)
    {
        // Arrange
        ConfigureStore();
        var image = new PersonalDataExportImage
        {
            ImageId = Guid.CreateVersion7(),
            ContentHash = digest
        };
        ConfigureSnapshot([image]);
        _imageStoreMock
            .Setup(store => store.OpenReadAsync(
                image.ImageId,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new MemoryStream([1]));

        // Act
        var exception = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _builder.BuildAsync(
                _workItem,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception.InnerException);
        _imageStoreMock.Verify(
            store => store.OpenReadAsync(
                image.ImageId,
                TestContext.Current.CancellationToken),
            Times.Once);
        VerifySnapshotAndStore();
    }

    [Fact]
    public async Task BuildAsync_WhenSnapshotFails_DoesNotReportAnArchive()
    {
        // Arrange
        ConfigureStore();
        var failure = new InvalidAuthenticationSessionException();
        _snapshotReaderMock
            .Setup(reader => reader.WriteAsync(
                _workItem.MemberId,
                It.IsAny<Stream>(),
                _manifest,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(failure);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(() => _builder.BuildAsync(
                _workItem,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(
            failure,
            exception);
        VerifySnapshotAndStore();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAsync_WhenStorageThrowsRawFailure_RemovesPathAndInnerException(bool accessDenied)
    {
        // Arrange
        Exception failure = accessDenied ? new UnauthorizedAccessException("/private/path") : new IOException("/private/path");
        _exportStoreMock
            .Setup(store => store.WriteAsync(
                _workItem.ExportId,
                _workItem.LeaseId,
                It.IsAny<Func<PersonalDataExportWriteContext, CancellationToken, Task>>(),
                _options.MaximumArchiveBytes,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(failure);

        // Act
        var exception = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _builder.BuildAsync(
                _workItem,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            "/private/path",
            exception.ToString());
        VerifyStore();
        _snapshotReaderMock.VerifyNoOtherCalls();
        _imageStoreMock.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _archive.Dispose();
        _manifest.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task BuildAsync_WhenReadmeCannotBeWritten_RejectsTheArchiveWithoutExposingStoragePaths()
    {
        // Arrange
        using var archiveStream = new FailingReadmeArchiveStream();
        ConfigureStore(archiveStream);
        ConfigureSnapshot([]);

        // Act
        var exception = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => _builder.BuildAsync(
                _workItem,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain(
            "private-path",
            exception.ToString());
        Assert.Null(exception.InnerException);
        VerifySnapshotAndStore();
    }

    private void ConfigureStore(Stream? archiveStream = null)
    {
        _exportStoreMock
            .Setup(store => store.WriteAsync(
                _workItem.ExportId,
                _workItem.LeaseId,
                It.IsAny<Func<PersonalDataExportWriteContext, CancellationToken, Task>>(),
                _options.MaximumArchiveBytes,
                TestContext.Current.CancellationToken))
            .Returns(async (
                Guid exportId,
                Guid archiveId,
                Func<PersonalDataExportWriteContext, CancellationToken, Task> writeAsync,
                long maximumBytes,
                CancellationToken cancellationToken) =>
            {
                await writeAsync(
                    new PersonalDataExportWriteContext
                    {
                        Archive = archiveStream ?? _archive,
                        ImageManifest = _manifest
                    },
                    cancellationToken);

                return _archive.Length;
            });
    }

    private void ConfigureSnapshot(PersonalDataExportImage?[] images)
    {
        _snapshotReaderMock
            .Setup(reader => reader.WriteAsync(
                _workItem.MemberId,
                It.IsAny<Stream>(),
                _manifest,
                TestContext.Current.CancellationToken))
            .Returns(async (
                Guid memberId,
                Stream destination,
                Stream manifest,
                CancellationToken cancellationToken) =>
            {
                await destination.WriteAsync(
                    Encoding.UTF8.GetBytes("{\"schemaVersion\":1}"),
                    cancellationToken);
                await JsonSerializer.SerializeAsync(
                    manifest,
                    images,
                    _jsonOptions,
                    cancellationToken);

                return _snapshotAt;
            });
    }

    private void VerifySnapshotAndStore()
    {
        _snapshotReaderMock.Verify(
            reader => reader.WriteAsync(
                _workItem.MemberId,
                It.IsAny<Stream>(),
                _manifest,
                TestContext.Current.CancellationToken),
            Times.Once);
        _snapshotReaderMock.VerifyNoOtherCalls();
        _imageStoreMock.VerifyNoOtherCalls();
        VerifyStore();
    }

    private void VerifyStore()
    {
        _exportStoreMock.Verify(
            store => store.WriteAsync(
                _workItem.ExportId,
                _workItem.LeaseId,
                It.IsAny<Func<PersonalDataExportWriteContext, CancellationToken, Task>>(),
                _options.MaximumArchiveBytes,
                TestContext.Current.CancellationToken),
            Times.Once);
        _exportStoreMock.VerifyNoOtherCalls();
    }
}
