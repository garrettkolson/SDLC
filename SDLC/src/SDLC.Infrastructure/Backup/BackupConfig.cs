namespace SDLC.Infrastructure.Backup;

public class BackupConfig
{
    public string BackupsDirectory { get; set; } = "backups";
    public string DatabaseFile { get; set; } = "sdlc.db";
    public string ArtifactsDirectory { get; set; } = "artifacts";
    public int RetentionDays { get; set; } = 30;
    public bool EnableAutoCleanup { get; set; } = true;
}
