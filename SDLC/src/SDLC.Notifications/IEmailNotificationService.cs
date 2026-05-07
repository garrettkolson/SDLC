using SDLC.Infrastructure;

namespace SDLC.Notifications;

public interface IEmailNotificationService
{
    Task SendApprovalRequestAsync(StageGate gate, CancellationToken ct = default);
}
