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
    private readonly HashSet<Guid> _notified = new();

    /// <summary>Gate IDs already notified — resets on restart (fine for reminders).</summary>
    public HashSet<Guid> SeenGates => _notified;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Gate reminder service starting, interval={Interval}", _interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder sweep failed");
            }

            await Task.Delay(_interval, ct);
        }

        logger.LogInformation("Gate reminder service stopping");
    }

    /// <summary>Single sweep iteration — used by tests and ExecuteAsync.</summary>
    public async Task RunSweepAsync()
    {
        var pendingGates = await gateStore.GetAllPendingAsync();
        var stale = pendingGates
            .Where(g => DateTimeOffset.UtcNow - g.CreatedAt > _staleAfter)
            .ToList();

        var toNotify = stale.Where(g => !_notified.Contains(g.GateId)).ToList();
        foreach (var gate in toNotify)
        {
            try
            {
                await notifications.SendApprovalRequestAsync(gate);
                _notified.Add(gate.GateId);
                logger.LogInformation("Reminder sent for stale gate {GateId} (age={Age:h\\h m\\m})",
                    gate.GateId, DateTimeOffset.UtcNow - gate.CreatedAt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reminder failed for gate {GateId}", gate.GateId);
            }
        }

        _notified.IntersectWith(pendingGates.Select(g => g.GateId).ToHashSet());

        if (toNotify.Count > 0)
            logger.LogInformation("Sent {Count} gate reminders", toNotify.Count);
    }
}
