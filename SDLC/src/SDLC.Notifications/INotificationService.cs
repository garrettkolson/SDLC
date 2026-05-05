using System.Net.Http.Json;
using SDLC.Infrastructure;

namespace SDLC.Notifications;

public interface INotificationService
{
    Task SendApprovalRequestAsync(StageGate gate);
}
