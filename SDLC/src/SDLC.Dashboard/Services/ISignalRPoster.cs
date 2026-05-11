namespace SDLC.Dashboard.Services;

/// <summary>Sends notifications to SignalR clients without depending on IHubContext directly.</summary>
public interface ISignalRPoster
{
    Task PushGateResolvedAsync(Hubs.GateResolvedMessage msg, CancellationToken ct = default);
    Task PushRunStateChangedAsync(Hubs.RunStateChangedMessage msg, CancellationToken ct = default);
}
