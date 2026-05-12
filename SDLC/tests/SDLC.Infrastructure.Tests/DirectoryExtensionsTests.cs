using FluentAssertions;
using SDLC.Infrastructure.Backup;

namespace SDLC.Infrastructure.Tests;

[TestFixture]
public class DirectoryExtensionsTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"copytest-{Guid.NewGuid()}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public void CopyWithContents_CopiesFlatFile()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "file.txt"), "hello");

        DirectoryExtensions.CopyWithContents(source, dest, overwrite: true);

        File.ReadAllText(Path.Combine(dest, "file.txt")).Should().Be("hello");
    }

    [Test]
    public void CopyWithContents_CopiesRecursiveFiles()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);

        var subDir = Path.Combine(source, "sub", "nested");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "deep.txt"), "deep content");
        File.WriteAllText(Path.Combine(source, "root.txt"), "root content");

        DirectoryExtensions.CopyWithContents(source, dest, overwrite: true);

        File.ReadAllText(Path.Combine(dest, "sub", "nested", "deep.txt")).Should().Be("deep content");
        File.ReadAllText(Path.Combine(dest, "root.txt")).Should().Be("root content");
    }

    [Test]
    public void CopyWithContents_CreatesDestDirectory()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);

        DirectoryExtensions.CopyWithContents(source, dest, overwrite: true);

        Directory.Exists(dest).Should().BeTrue();
    }

    [Test]
    public void CopyWithContents_Overwrite_True_Overwrites()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);

        var srcFile = Path.Combine(source, "file.txt");
        var dstFile = Path.Combine(dest, "file.txt");
        File.WriteAllText(srcFile, "new content");
        File.WriteAllText(dstFile, "old content");

        DirectoryExtensions.CopyWithContents(source, dest, overwrite: true);

        File.ReadAllText(dstFile).Should().Be("new content");
    }

    [Test]
    public void CopyWithContents_Overwrite_False_ThrowsIOException()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);

        var srcFile = Path.Combine(source, "file.txt");
        var dstFile = Path.Combine(dest, "file.txt");
        File.WriteAllText(srcFile, "new content");
        File.WriteAllText(dstFile, "old content");

        var act = () => DirectoryExtensions.CopyWithContents(source, dest, overwrite: false);
        act.Should().Throw<IOException>();
    }

    [Test]
    public void CopyWithContents_EmptySource_CreatesDest()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);

        DirectoryExtensions.CopyWithContents(source, dest, overwrite: true);

        Directory.Exists(dest).Should().BeTrue();
    }

    [Test]
    public void CopyWithContents_CopiedFileHasCorrectContent()
    {
        var source = Path.Combine(_tempDir, "src");
        var dest = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(source);

        var content = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE };
        File.WriteAllBytes(Path.Combine(source, "binary.bin"), content);

        DirectoryExtensions.CopyWithContents(source, dest, overwrite: true);

        File.ReadAllBytes(Path.Combine(dest, "binary.bin")).Should().BeEquivalentTo(content);
    }
}
