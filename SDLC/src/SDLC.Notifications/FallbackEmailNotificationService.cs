using Microsoft.Extensions.Logging;
using SDLC.Infrastructure;

namespace SDLC.Notifications;

/// <summary>
/// Email notification fallback — logs gate data when Slack is down.
/// Replace with SMTP/SendGrid implementation in a follow-up.
/// </summary>
public class FallbackEmailNotificationService(
    ILogger<FallbackEmailNotificationService> logger)
    : IEmailNotificationService
{
    public Task SendApprovalRequestAsync(StageGate gate, CancellationToken ct = default)
    {
        logger.LogWarning(
            "[EMAIL FALLBACK] Approval request for gate {GateId}, stage {Stage}, run {RunId}. Review at: {Url}",
            gate.GateId, gate.Stage, gate.RunId,
            $"http://localhost:8080/gate/{gate.GateId}");
        return Task.CompletedTask;
    }
}
