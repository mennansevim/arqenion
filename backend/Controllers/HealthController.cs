using System.Diagnostics;
using Arqenion.Api.Data;
using Arqenion.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Arqenion.Api.Controllers;

// Hazır olma denetimi (yük dengeleyici / izleme için). Bağlantı bilgisi ya da gizli değer döndürmez.
//   200 ok        : her şey çalışıyor
//   200 degraded  : AI servisi yok; kararlar, puan ve klinik kart yine çalışır (açıklama hazır metinden)
//   503 down      : veritabanına ulaşılamıyor ya da uygulanmamış migration var
[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly ArqenionDbContext _db;
    private readonly FlowModelStore _model;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    public HealthController(ArqenionDbContext db, FlowModelStore model, IHttpClientFactory http, IConfiguration config)
    {
        _db = db;
        _model = model;
        _http = http;
        _config = config;
    }

    [HttpGet("ready")]
    public async Task<ActionResult> Ready(CancellationToken ct)
    {
        var database = await Check(async () =>
        {
            if (!await _db.Database.CanConnectAsync(ct)) return (false, "bağlantı yok");
            if (!_db.Database.IsRelational()) return (true, "bellek içi");
            var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();
            return pending.Count == 0 ? (true, "migration güncel") : (false, $"uygulanmamış migration: {string.Join(", ", pending)}");
        });

        var baseUrl = _config["AiGateway:BaseUrl"];
        var ai = string.IsNullOrWhiteSpace(baseUrl)
            ? new CheckResult(false, "kapalı (AiGateway:BaseUrl boş)", 0)
            : await Check(async () =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                var res = await _http.CreateClient().GetAsync($"{baseUrl.TrimEnd('/')}/health", cts.Token);
                if (!res.IsSuccessStatusCode) return (false, $"HTTP {(int)res.StatusCode}");
                var body = await res.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken: cts.Token);
                return (true, $"yazıcı: {body?.GetValueOrDefault("writer")}");
            });

        var status = !database.Ok ? "down" : ai.Ok ? "ok" : "degraded";
        var payload = new
        {
            status,
            checks = new
            {
                database,
                model = new { ok = true, detail = $"v{_model.Version}, {_model.PlayableNodes.Count} düğüm" },
                aiGateway = ai
            }
        };
        return database.Ok ? Ok(payload) : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }

    public sealed record CheckResult(bool Ok, string Detail, long Ms);

    private static async Task<CheckResult> Check(Func<Task<(bool Ok, string Detail)>> probe)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (ok, detail) = await probe();
            return new CheckResult(ok, detail, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new CheckResult(false, ex is OperationCanceledException ? "zaman aşımı" : ex.GetType().Name, sw.ElapsedMilliseconds);
        }
    }
}
