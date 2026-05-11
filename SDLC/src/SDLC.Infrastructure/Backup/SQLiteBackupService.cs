using Microsoft.Extensions.Logging;
using SDLC.Infrastructure.Backup;

namespace SDLC.Infrastructure.Backup;

public class SQLiteBackupService
{
    private readonly IFileManager _fileManager;
    private readonly BackupConfig _config;
    private readonly ILogger<SQLiteBackupService>? _logger;

    public SQLiteBackupService(
        IFileManager fileManager,
        BackupConfig config,
        ILogger<SQLiteBackupService>? logger = null)
    {
        _fileManager = fileManager;
        _config = config;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(CancellationToken ct = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backupDir = Path.Combine(_config.BackupsDirectory, $"sdlc-{timestamp}");

        _logger?.LogInformation("Starting backup to {BackupDir}", backupDir);

        await _fileManager.CreateDirectoryAsync(backupDir);

        // Copy SQLite database file -- safe with WAL mode when no active writer
        var dbFileName = Path.GetFileName(_config.DatabaseFile);
        await _fileManager.CopyFileAsync(
            _config.DatabaseFile,
            Path.Combine(backupDir, dbFileName),
            overwrite: true);

        // Also copy WAL/shm sidecar files if they exist
        var walFile = _config.DatabaseFile + "-wal";
        var shmFile = _config.DatabaseFile + "-shm";
        if (_fileManager.FileExists(walFile))
        {
            await _fileManager.CopyFileAsync(walFile, Path.Combine(backupDir, walFile), overwrite: true);
        }
        if (_fileManager.FileExists(shmFile))
        {
            await _fileManager.CopyFileAsync(shmFile, Path.Combine(backupDir, shmFile), overwrite: true);
        }

        // Copy artifacts directory
        if (_fileManager.DirectoryExists(_config.ArtifactsDirectory))
        {
            await _fileManager.CopyDirectoryAsync(
                _config.ArtifactsDirectory,
                Path.Combine(backupDir, _config.ArtifactsDirectory),
                true);
        }

        // Cleanup old backups
        if (_config.EnableAutoCleanup)
        {
            await CleanupOldBackupsAsync();
        }

        _logger?.LogInformation("Backup completed: {BackupDir}", backupDir);
        return backupDir;
    }

    private async Task CleanupOldBackupsAsync()
    {
        if (!_fileManager.DirectoryExists(_config.BackupsDirectory))
            return;

        var cutoff = DateTime.UtcNow.AddDays(-_config.RetentionDays);
        var backupEntries = _fileManager.GetDirectories(_config.BackupsDirectory);

        foreach (var entry in backupEntries)
        {
            var lastWrite = _fileManager.GetLastWriteTime(entry);
            if (lastWrite < cutoff)
            {
                try
                {
                    await _fileManager.DeleteDirectoryAsync(entry);
                    _logger?.LogInformation("Removed old backup: {Entry}", entry);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to remove old backup: {Entry}", entry);
                }
            }
        }
    }
}
