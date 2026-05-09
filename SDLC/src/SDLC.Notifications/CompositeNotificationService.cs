using Microsoft.Extensions.Logging;
using SDLC.Infrastructure;

namespace SDLC.Notifications;

/// <summary>
/// Tries Slack first, falls through to Email on failure.
/// </summary>
public class CompositeNotificationService(
    SlackNotificationService slack,
    IEmailNotificationService? email = null,
    ILogger<CompositeNotificationService>? logger = null)
    : INotificationService
{
    public async Task SendApprovalRequestAsync(StageGate gate)
    {
        try
        {
            await slack.SendApprovalRequestAsync(gate);
            return;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Slack notification failed, trying email fallback");
            if (email != null)
            {
                try
                {
                    await email.SendApprovalRequestAsync(gate);
                    return;
                }
                catch (Exception emailEx)
                {
                    logger?.LogError(emailEx, "Email fallback also failed");
                }
            }
            throw new CompositeNotificationException("All notification services failed", null);
        }
    }
}

public class CompositeNotificationException : Exception
{
    public CompositeNotificationException(string message, Exception? inner) : base(message, inner) { }
}
