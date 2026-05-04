using SDLC.Infrastructure;

namespace SDLC.Notifications;

public interface INotificationService
{
    Task SendApprovalRequestAsync(StageGate gate);
}

public class SlackNotificationService : INotificationService
{
    private readonly string _webhookUrl;

    public SlackNotificationService(string webhookUrl)
    {
        _webhookUrl = webhookUrl;
    }

    public Task SendApprovalRequestAsync(StageGate gate)
    {
        // TODO: POST to Slack webhook with gate details
        return Task.CompletedTask;
    }
}
