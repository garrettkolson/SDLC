using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDLC.Infrastructure;

namespace SDLC.Notifications;

public class GateReminderService(
    IStageGateStore gateStore,
    INotificationService notifications,
    ILogger<GateReminderService> logger)
    : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(4);
    private readonly TimeSpan _staleAfter = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Gate reminder service starting, interval={Interval}", _interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pendingGates = await gateStore.GetAllPendingAsync();
                var stale = pendingGates
                    .Where(g => DateTimeOffset.UtcNow - g.CreatedAt > _staleAfter)
                    .ToList();

                foreach (var gate in stale)
                {
                    try
                    {
                        await notifications.SendApprovalRequestAsync(gate);
                        logger.LogInformation("Reminder sent for stale gate {GateId} (age={Age:h\\h m\\m})",
                            gate.GateId, DateTimeOffset.UtcNow - gate.CreatedAt);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Reminder failed for gate {GateId}", gate.GateId);
                    }
                }

                if (stale.Count > 0)
                    logger.LogInformation("Sent {Count} gate reminders", stale.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder sweep failed");
            }

            await Task.Delay(_interval, ct);
        }

        logger.LogInformation("Gate reminder service stopping");
    }
}
