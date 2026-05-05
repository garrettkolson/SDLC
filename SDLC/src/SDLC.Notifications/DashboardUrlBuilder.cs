namespace SDLC.Notifications;

public class DashboardUrlBuilder(string baseUrl)
{
    public string ForGate(Guid gateId) => $"{baseUrl.TrimEnd('/')}/gate/{gateId}";
}
