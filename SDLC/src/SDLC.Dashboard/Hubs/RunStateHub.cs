using Microsoft.AspNetCore.SignalR;

namespace SDLC.Dashboard.Hubs;

public class RunStateHub : Hub
{
    public Task SubscribeToRun(Guid runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString());

    public Task UnsubscribeFromRun(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString());
}
