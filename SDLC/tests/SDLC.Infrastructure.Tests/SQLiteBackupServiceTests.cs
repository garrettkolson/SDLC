using SDLC.Infrastructure.Backup;
using FluentAssertions;

namespace SDLC.Infrastructure.Tests;

[TestFixture, SingleThreaded]
public class SQLiteBackupServiceTests
{
    private SQLiteBackupService _service = null!;
    private FakeFileManager _fileManager = null!;

    [SetUp]
    public void SetUp()
    {
        _fileManager = new FakeFileManager();
        var config = new BackupConfig
        {
            BackupsDirectory = "/test/backups",
            DatabaseFile = "/test/sdlc.db",
            ArtifactsDirectory = "/test/artifacts",
            RetentionDays = 30,
            EnableAutoCleanup = true
        };
        _service = new SQLiteBackupService(_fileManager, config);
    }

    [Test]
    public void Constructor_AcceptsDependencies()
    {
        _service.Should().NotBeNull();
    }

    [Test]
    public async Task CreateBackupAsync_IncrementsBackupCount()
    {
        var result = await _service.CreateBackupAsync();

        result.Should().NotBeNullOrEmpty();
        _fileManager.CreatedDirectories.Should().ContainSingle();
    }

    [Test]
    public async Task CreateBackupAsync_CopiesDatabaseFile()
    {
        await _service.CreateBackupAsync();

        _fileManager.CopiedFiles.Should().ContainSingle(
            c => Path.GetFileName(c.Source) == "sdlc.db");
    }

    [Test]
    public async Task CreateBackupAsync_CopiesArtifactsDirectory()
    {
        await _service.CreateBackupAsync();

        _fileManager.CopiedDirectories.Should().ContainSingle(
            c => c.Source == "/test/artifacts");
    }

    [Test]
    public async Task CreateBackupAsync_CleansUpOldBackups()
    {
        _fileManager.FakeDirectories.Add("/test/backups/sdlc-20260101-000000");
        _fileManager.SetLastWriteTime("/test/backups/sdlc-20260101-000000", DateTime.UtcNow.AddDays(-60));

        await _service.CreateBackupAsync();

        _fileManager.DeletedDirectories.Should().Contain("/test/backups/sdlc-20260101-000000");
    }

    [Test]
    public async Task CreateBackupAsync_CopiesWalAndShmWhenPresent()
    {
        _fileManager.FakeWalExists = true;
        _fileManager.FakeShmExists = true;

        await _service.CreateBackupAsync();

        _fileManager.CopiedFiles.Should().Contain(
            c => Path.GetFileName(c.Source) == "sdlc.db-wal");
        _fileManager.CopiedFiles.Should().Contain(
            c => Path.GetFileName(c.Source) == "sdlc.db-shm");
    }

    [Test]
    public async Task CreateBackupAsync_DoesNotCopyAbsentWalFiles()
    {
        await _service.CreateBackupAsync();

        _fileManager.CopiedFiles.Should().NotContain(
            c => c.Source == "/test/sdlc.db-wal");
    }
}

public class FakeFileManager : IFileManager
{
    public List<CopiedFile> CopiedFiles { get; } = new();
    public List<CopiedDir> CopiedDirectories { get; } = new();
    public List<string> CreatedDirectories { get; } = new();
    public List<string> DeletedDirectories { get; } = new();
    public Dictionary<string, DateTime> FakeLastWrites { get; } = new();
    public List<string> FakeDirectories { get; } = new();
    public bool FakeWalExists { get; set; }
    public bool FakeShmExists { get; set; }

    public Task CopyDirectoryAsync(string source, string destination, bool overwrite = true)
    {
        CopiedDirectories.Add(new CopiedDir(source));
        return Task.CompletedTask;
    }

    public Task CopyFileAsync(string source, string destination, bool overwrite = false)
    {
        CopiedFiles.Add(new CopiedFile(source));
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive = true)
    {
        DeletedDirectories.Add(path);
        FakeDirectories.Remove(path);
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path)
    {
        CreatedDirectories.Add(path);
        FakeDirectories.Add(path);
        return Task.CompletedTask;
    }

    public bool DirectoryExists(string path) => path == "/test/artifacts" || path == "/test/backups" || FakeDirectories.Contains(path);
    public bool FileExists(string path) => path switch
    {
        "/test/sdlc.db-wal" => FakeWalExists,
        "/test/sdlc.db-shm" => FakeShmExists,
        _ => false
    };
    public DateTime GetLastWriteTime(string path) => FakeLastWrites.GetValueOrDefault(path, DateTime.UtcNow);
    public string[] GetDirectories(string path) => FakeDirectories
        .Where(d => d.StartsWith(path + "/") || d == path)
        .ToArray();
    public void SetLastWriteTime(string path, DateTime time) => FakeLastWrites[path] = time;
}

public record CopiedFile(string Source);
public record CopiedDir(string Source);
