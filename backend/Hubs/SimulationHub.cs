using Arqenion.Api.Data;
using Arqenion.Api.Models;
using Arqenion.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Arqenion.Api.Hubs;

// Bağlantı jeton ister (?access_token=); kursiyer yalnız kendi seansının mesajlarını (ScoreUpdate, AiFeedback) alır.
[Authorize]
public sealed class SimulationHub : Hub
{
    private readonly ArqenionDbContext _db;

    public SimulationHub(ArqenionDbContext db) => _db = db;

    public async Task JoinSession(string sessionId)
    {
        var session = await _db.Sessions.FindAsync(sessionId);
        var user = Context.User!;
        if (session is null || (session.UserId != user.UserId() && !user.IsInstructor()))
            throw new HubException("Seans bulunamadı.");
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public Task LeaveSession(string sessionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
