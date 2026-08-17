using System;
using System.IO;
using Microsoft.Data.Sqlite;
using slskd.Shares;
using Xunit;

namespace slskd.Tests.Unit.Shares;

public class SqliteShareRepositoryTests : IDisposable
{
    private readonly string connectionString;
    private readonly SqliteShareRepository repository;
    private readonly string temp;

    public SqliteShareRepositoryTests()
    {
        temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
        Directory.CreateDirectory(temp);

        connectionString = $"Data Source={Path.Combine(temp, "shares.db")};Pooling=False";
        repository = new SqliteShareRepository(connectionString);
        repository.Create(discardExisting: true);
    }

    public void Dispose()
    {
        repository.Dispose();
        Directory.Delete(temp, recursive: true);
    }

    [Fact]
    public void TryMarkFileAsSeen_Updates_Scan_Timestamp_When_Identity_Is_Unchanged()
    {
        var touchedAt = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc).AddTicks(9012);
        InsertFile(touchedAt, timestamp: 10);

        var found = repository.TryMarkFileAsSeen("Music\\file.flac", "/music/file.flac", 123, touchedAt, timestamp: 20);

        Assert.True(found);
        Assert.Equal(20, ReadTimestamp());
        Assert.Equal("2026-01-02T03:04:05.6789012Z", ReadTouchedAt());
    }

    [Fact]
    public void TryMarkFileAsSeen_Does_Not_Match_A_Different_Time_On_The_Same_Date()
    {
        var touchedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        InsertFile(touchedAt, timestamp: 10);

        var found = repository.TryMarkFileAsSeen(
            "Music\\file.flac",
            "/music/file.flac",
            123,
            touchedAt.AddSeconds(1),
            timestamp: 20);

        Assert.False(found);
        Assert.Equal(10, ReadTimestamp());
    }

    private void InsertFile(DateTime touchedAt, long timestamp)
    {
        var file = new Soulseek.File(
            1,
            "Music\\file.flac",
            123,
            "flac",
            Array.Empty<Soulseek.FileAttribute>());

        repository.InsertFile(file.Filename, "/music/file.flac", touchedAt, file, timestamp);
    }

    private long ReadTimestamp()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = new SqliteCommand("SELECT timestamp FROM files", connection);
        return (long)command.ExecuteScalar();
    }

    private string ReadTouchedAt()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = new SqliteCommand("SELECT touchedAt FROM files", connection);
        return (string)command.ExecuteScalar();
    }
}
