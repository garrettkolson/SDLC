using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SDLC.Infrastructure.Backup;

namespace SDLC.Infrastructure.Backup;

public class ScheduledBackupService : BackgroundService
{
    private readonly SQLiteBackupService _backupService;
    private readonly BackupConfig _config;
    private readonly ILogger<ScheduledBackupService> _logger;

    public ScheduledBackupService(
        SQLiteBackupService backupService,
        IOptions<BackupConfig> config,
        ILogger<ScheduledBackupService> logger)
    {
        _backupService = backupService;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled backup service starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddDays(1); // Tomorrow at midnight UTC

                var delay = nextRun - now;
                _logger.LogInformation("Next backup scheduled at {NextRun} (in {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await _backupService.CreateBackupAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup failed.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Scheduled backup service stopping.");
    }
}
