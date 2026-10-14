using Microsoft.AspNetCore.SignalR;

namespace Arqenion.Api.Hubs;

public sealed class SimulationHub : Hub
{
    public Task JoinSession(string sessionId)
        => Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public Task LeaveSession(string sessionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
