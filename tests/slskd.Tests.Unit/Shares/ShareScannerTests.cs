using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using slskd.Files;
using slskd.Shares;
using Xunit;

namespace slskd.Tests.Unit.Shares;

public class ShareScannerTests : IDisposable
{
    private readonly Mock<ISoulseekFileFactory> fileFactoryMock;
    private readonly SqliteShareRepository repository;
    private readonly ShareScanner scanner;
    private readonly string sharePath;
    private readonly string temp;

    public ShareScannerTests()
    {
        temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
        Directory.CreateDirectory(temp);
        sharePath = Path.Combine(temp, "share");
        Directory.CreateDirectory(sharePath);

        var databasePath = Path.Combine(temp, "shares.db");
        repository = new SqliteShareRepository($"Data Source={databasePath};Pooling=False");
        repository.Create(discardExisting: true);

        var optionsMonitorMock = new Mock<IOptionsMonitor<Options>>();
        optionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());
        var fileService = new FileService(optionsMonitorMock.Object);

        fileFactoryMock = new Mock<ISoulseekFileFactory>();
        fileFactoryMock
            .Setup(factory => factory.Create(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string filename, string maskedFilename) => new Soulseek.File(
                1,
                maskedFilename,
                new FileInfo(filename).Length,
                Path.GetExtension(filename).TrimStart('.'),
                Array.Empty<Soulseek.FileAttribute>()));

        scanner = new ShareScanner(
            workerCount: 1,
            fileService,
            soulseekFileFactory: fileFactoryMock.Object);
    }

    public void Dispose()
    {
        repository.Dispose();
        Directory.Delete(temp, recursive: true);
    }

    [Fact]
    public async Task ScanAsync_Reuses_Unchanged_Metadata_And_Parses_New_Or_Changed_Files()
    {
        var original = Path.Combine(sharePath, "original.flac");
        await File.WriteAllTextAsync(original, "original");
        File.SetLastWriteTimeUtc(original, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        await ScanAsync();

        fileFactoryMock.Verify(factory => factory.Create(original, It.IsAny<string>()), Times.Once);
        Assert.Equal(1, repository.CountFiles());

        await ScanAsync();

        fileFactoryMock.Verify(factory => factory.Create(original, It.IsAny<string>()), Times.Once);

        var added = Path.Combine(sharePath, "added.flac");
        await File.WriteAllTextAsync(added, "added");
        File.SetLastWriteTimeUtc(added, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        await ScanAsync();

        fileFactoryMock.Verify(factory => factory.Create(original, It.IsAny<string>()), Times.Once);
        fileFactoryMock.Verify(factory => factory.Create(added, It.IsAny<string>()), Times.Once);
        Assert.Equal(2, repository.CountFiles());

        File.SetLastWriteTimeUtc(original, new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc));

        await ScanAsync();

        fileFactoryMock.Verify(factory => factory.Create(original, It.IsAny<string>()), Times.Exactly(2));
        fileFactoryMock.Verify(factory => factory.Create(added, It.IsAny<string>()), Times.Once);

        File.Delete(added);

        await ScanAsync();

        fileFactoryMock.Verify(factory => factory.Create(original, It.IsAny<string>()), Times.Exactly(2));
        fileFactoryMock.Verify(factory => factory.Create(added, It.IsAny<string>()), Times.Once);
        Assert.Equal(1, repository.CountFiles());
    }

    [Fact]
    public async Task ScanAsync_Reparses_File_When_Size_Changes_Without_Mtime_Changing()
    {
        var filename = Path.Combine(sharePath, "resized.flac");
        var touchedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await File.WriteAllTextAsync(filename, "before");
        File.SetLastWriteTimeUtc(filename, touchedAt);

        await ScanAsync();

        await File.WriteAllTextAsync(filename, "after with a different size");
        File.SetLastWriteTimeUtc(filename, touchedAt);

        await ScanAsync();

        fileFactoryMock.Verify(factory => factory.Create(filename, It.IsAny<string>()), Times.Exactly(2));
    }

    private Task ScanAsync()
    {
        var options = new Options.SharesOptions
        {
            Directories = [sharePath],
        };

        return scanner.ScanAsync([new Share(sharePath)], options, repository);
    }
}
