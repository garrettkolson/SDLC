namespace SDLC.Infrastructure.Backup;

public interface IFileManager
{
    Task CopyDirectoryAsync(string source, string destination, bool overwrite = true);
    Task CopyFileAsync(string source, string destination, bool overwrite = false);
    Task DeleteDirectoryAsync(string path, bool recursive = true);
    Task CreateDirectoryAsync(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    DateTime GetLastWriteTime(string path);
    string[] GetDirectories(string path);
}
