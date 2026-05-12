using FluentAssertions;
using SDLC.Infrastructure.Backup;

namespace SDLC.Infrastructure.Tests;

[TestFixture]
public class FileSystemServiceTests
{
    private FileSystemService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fss-test-{Guid.NewGuid()}");
        _service = new FileSystemService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public async Task CopyDirectoryAsync_CopiesAllFiles()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "b");

        await _service.CopyDirectoryAsync(source, dest);

        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("a");
        File.ReadAllText(Path.Combine(dest, "sub", "b.txt")).Should().Be("b");
    }

    [Test]
    public async Task CopyFileAsync_CopiesFile()
    {
        var source = Path.Combine(_tempDir, "src.txt");
        var dest = Path.Combine(_tempDir, "dst.txt");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(source, "content");

        await _service.CopyFileAsync(source, dest);

        File.ReadAllText(dest).Should().Be("content");
    }

    [Test]
    public async Task CopyFileAsync_Overwrite_False_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        var source = Path.Combine(_tempDir, "src.txt");
        var dest = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(source, "new");
        File.WriteAllText(dest, "old");

        var act = () => _service.CopyFileAsync(source, dest, overwrite: false);
        await act.Should().ThrowAsync<IOException>();
    }

    [Test]
    public async Task CopyFileAsync_Overwrite_True_Overwrites()
    {
        Directory.CreateDirectory(_tempDir);
        var source = Path.Combine(_tempDir, "src.txt");
        var dest = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(source, "new");
        File.WriteAllText(dest, "old");

        await _service.CopyFileAsync(source, dest, overwrite: true);

        File.ReadAllText(dest).Should().Be("new");
    }

    [Test]
    public void DirectoryExists_ReturnsTrue_ForExistingDirectory()
    {
        Directory.CreateDirectory(_tempDir);
        _service.DirectoryExists(_tempDir).Should().BeTrue();
    }

    [Test]
    public void DirectoryExists_ReturnsFalse_ForNonExistentDirectory()
    {
        _service.DirectoryExists(Path.Combine(_tempDir, "nonexistent")).Should().BeFalse();
    }

    [Test]
    public void FileExists_ReturnsTrue_ForExistingFile()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(file, "test");
        _service.FileExists(file).Should().BeTrue();
    }

    [Test]
    public void FileExists_ReturnsFalse_ForNonExistentFile()
    {
        _service.FileExists(Path.Combine(_tempDir, "nonexistent.txt")).Should().BeFalse();
    }

    [Test]
    public void CreateDirectoryAsync_CreatesDirectory()
    {
        var path = Path.Combine(_tempDir, "newdir");
        Directory.CreateDirectory(_tempDir);
        _service.CreateDirectoryAsync(path).Wait();
        Directory.Exists(path).Should().BeTrue();
    }

    [Test]
    public void DeleteDirectoryAsync_DeletesDirectory()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "delme");
        Directory.CreateDirectory(path);
        _service.DeleteDirectoryAsync(path).Wait();
        Directory.Exists(path).Should().BeFalse();
    }

    [Test]
    public void GetLastWriteTime_ReturnsFileTime()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "time.txt");
        File.WriteAllText(file, "test");
        var expected = File.GetLastWriteTime(file);
        var actual = _service.GetLastWriteTime(file);
        actual.Should().BeCloseTo(expected, TimeSpan.FromMilliseconds(100));
    }

    [Test]
    public void GetDirectories_ReturnsSubdirectories()
    {
        Directory.CreateDirectory(_tempDir);
        var dir = Path.Combine(_tempDir, "parent");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "child1"));
        Directory.CreateDirectory(Path.Combine(dir, "child2"));

        var result = _service.GetDirectories(dir);
        result.Length.Should().BeGreaterThanOrEqualTo(2);
        result.Should().Contain(d => d.Contains("child1"));
        result.Should().Contain(d => d.Contains("child2"));
    }
}
