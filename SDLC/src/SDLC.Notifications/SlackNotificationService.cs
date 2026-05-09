using System.Net.Http.Json;
using SDLC.Infrastructure;

namespace SDLC.Notifications;

public class SlackNotificationService(
    IHttpClientFactory httpClientFactory,
    DashboardUrlBuilder urls)
    : INotificationService
{
    public async Task SendApprovalRequestAsync(StageGate gate)
    {
        var reviewUrl = urls.ForGate(gate.GateId);

        var payload = new
        {
            text = $"SDLC Stage Gate — {gate.Stage} requires review",
            blocks = new object[]
            {
                new { type = "section", text = new { type = "mrkdwn", text = $"*SDLC Stage Gate — {gate.Stage}*" } },
                new { type = "section", text = new { type = "mrkdwn", text = $"Run `{gate.RunId}` requires review before proceeding to the next stage." } },
                new { type = "actions", elements = new[]
                {
                    new { type = "button", text = new { type = "plain_text", text = "Review & Approve" }, url = reviewUrl },
                    new { type = "button", text = new { type = "plain_text", text = "Reject — Re-run Stage" }, url = reviewUrl }
                }}
            }
        };

        await httpClientFactory.CreateClient("slack").PostAsJsonAsync("/webhook/sdlc", payload);
    }
}
