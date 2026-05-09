namespace SDLC.Infrastructure.Backup;

public static class DirectoryExtensions
{
    public static void CopyWithContents(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, relative);
            var destDir2 = Path.GetDirectoryName(dest)!;
            if (!Directory.Exists(destDir2))
                Directory.CreateDirectory(destDir2);
            File.Copy(file, dest, overwrite);
        }
    }
}
