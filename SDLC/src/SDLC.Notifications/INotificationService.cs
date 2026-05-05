using System.Net.Http.Json;
using SDLC.Infrastructure;

namespace SDLC.Notifications;

public interface INotificationService
{
    Task SendApprovalRequestAsync(StageGate gate);
}

public class SlackNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookPath;

    public SlackNotificationService(HttpClient httpClient, string webhookPath)
    {
        _httpClient = httpClient;
        _webhookPath = webhookPath;
    }

    public async Task SendApprovalRequestAsync(StageGate gate)
    {
        var payload = new
        {
            gate.GateId,
            gate.RunId,
            gate.Stage,
            gate.Status,
            gate.Notes,
            channel = "#sdlc-gates"
        };

        await _httpClient.PostAsJsonAsync(_webhookPath, payload);
    }
}
