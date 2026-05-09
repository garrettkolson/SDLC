namespace SDLC.Infrastructure.Backup;

public class FileSystemService : IFileManager
{
    public Task CopyDirectoryAsync(string source, string destination, bool overwrite = true)
        => Task.Run(() => DirectoryExtensions.CopyWithContents(source, destination, overwrite));

    public Task CopyFileAsync(string source, string destination, bool overwrite = false)
        => Task.Run(() => File.Copy(source, destination, overwrite));

    public Task DeleteDirectoryAsync(string path, bool recursive = true)
        => Task.Run(() => Directory.Delete(path, recursive));

    public Task CreateDirectoryAsync(string path) => Task.Run(() => Directory.CreateDirectory(path));

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
}
