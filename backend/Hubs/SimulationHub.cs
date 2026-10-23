using Arqenion.Api.Data;
using Arqenion.Api.Models;
using Arqenion.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Arqenion.Api.Hubs;

// Bağlantı jeton ister (?access_token=); kursiyer yalnız kendi seansının mesajlarını (ScoreUpdate, AiFeedback) alır.
// VR bağlantı testi ekranı ?monitor=1 ile bağlanır, WatchVr ile cihaz etkinliğini (VrActivity, VrClient) dinler.
[Authorize]
public sealed class SimulationHub : Hub
{
    private readonly ArqenionDbContext _db;
    private readonly VrMonitor _monitor;

    public SimulationHub(ArqenionDbContext db, VrMonitor monitor)
    {
        _db = db;
        _monitor = monitor;
    }

    public override Task OnConnectedAsync()
    {
        _monitor.RecordHub(Context.GetHttpContext(), Context.User?.UserId(), Context.User?.Identity?.Name, "hub-connect", true, null, null);
        return base.OnConnectedAsync();
    }

    public async Task JoinSession(string sessionId)
    {
        var session = await _db.Sessions.FindAsync(sessionId);
        var user = Context.User!;
        if (session is null || (session.UserId != user.UserId() && !user.IsInstructor()))
        {
            _monitor.RecordHub(Context.GetHttpContext(), user.UserId(), user.Identity?.Name, "hub-join", false, sessionId, "Seans bulunamadı.");
            throw new HubException("Seans bulunamadı.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _monitor.RecordHub(Context.GetHttpContext(), user.UserId(), user.Identity?.Name, "hub-join", true, sessionId, null);
    }

    public Task LeaveSession(string sessionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);

    // Eğitmen bütün cihazları, kursiyer yalnız kendi hesabıyla gelen istekleri izler.
    public async Task<List<VrClientSnapshot>> WatchVr()
    {
        var user = Context.User!;
        var instructor = user.IsInstructor();
        await Groups.AddToGroupAsync(Context.ConnectionId,
            instructor ? VrMonitor.AllGroup : VrMonitor.UserGroup(user.UserId()!));
        return _monitor.Snapshot(user.UserId(), instructor);
    }
}
