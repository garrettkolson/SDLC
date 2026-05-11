using Microsoft.AspNetCore.SignalR;

namespace SDLC.Dashboard.Services;

public class SignalRPoster : ISignalRPoster
{
    private readonly IHubContext<SDLC.Dashboard.Hubs.RunStateHub> _hubContext;

    public SignalRPoster(IHubContext<SDLC.Dashboard.Hubs.RunStateHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PushGateResolvedAsync(Hubs.GateResolvedMessage msg, CancellationToken ct = default)
    {
        return Task.WhenAll(
            _hubContext.Clients.All.SendAsync("GateResolved", msg, ct),
            _hubContext.Clients.Group("runs").SendAsync("GateResolved", msg, ct));
    }

    public Task PushRunStateChangedAsync(Hubs.RunStateChangedMessage msg, CancellationToken ct = default)
    {
        return Task.WhenAll(
            _hubContext.Clients.All.SendAsync("RunStateChanged", msg, ct),
            _hubContext.Clients.Group("runs").SendAsync("RunStateChanged", msg, ct));
    }
}
