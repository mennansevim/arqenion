using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Arqenion.Api.Hubs;
using Arqenion.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Arqenion.Api.Services;

// VR bağlantı testi (simulator/vr.html): VR sözleşmesine ait uçlara gelen istekleri cihaz başına bellekte tutar
// ve izleme ekranına SignalR ile yayar. Eğitmen bütün cihazları, kursiyer yalnız kendi girişiyle gelenleri görür.
// Şifre ve jeton hiçbir zaman tutulmaz; giriş için yalnız kullanıcı adı ve sonuç. Süreç yeniden başlayınca liste boşalır.
public sealed class VrMonitor
{
    public const string MonitorHeader = "X-Arqenion-Monitor";   // izleme ekranının kendi istekleri kaydedilmez
    public const string DeviceHeader = "X-Arqenion-Device";     // cihaz adı (ör. SystemInfo.deviceModel); yoksa User-Agent
    public const string AllGroup = "vr:all";
    public static string UserGroup(string userId) => $"vr:user:{userId}";

    private const int MaxClients = 300;
    private const int MaxEntries = 200;
    private const int MaxBody = 4096;

    private readonly IHubContext<SimulationHub> _hub;
    private readonly ILogger<VrMonitor> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, Client> _clients = new();
    // Girişten önceki girişsiz istekler (sağlık, katalog, modeller) aynı cihazın son giriş yapan kullanıcısına yazılır.
    private readonly Dictionary<string, (string UserId, string Username)> _lastUser = new();
    private long _nextId;

    public VrMonitor(IHubContext<SimulationHub> hub, ILogger<VrMonitor> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    private sealed class Client
    {
        public required VrClientDto Info;
        public readonly LinkedList<VrEntryDto> Entries = new();
    }

    // ---------- HTTP ----------

    public static string? Classify(string method, string path)
    {
        var p = path.TrimEnd('/');
        var seg = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return (method, seg) switch
        {
            ("GET", ["api", "health"]) or ("GET", ["api", "health", "ready"]) => "health",
            ("POST", ["api", "auth", "login"]) => "login",
            ("GET", ["api", "procedure"]) => "procedure",
            ("GET", ["api", "assets", "manifest"]) => "manifest",
            ("GET", ["assets", "catalog", _]) => "asset",
            ("POST", ["api", "sessions"]) => "session",
            ("GET", ["api", "sessions", _]) => "resume",
            ("POST", ["api", "sessions", _, "events"]) => "event",
            ("POST", ["api", "sessions", _, "complete"]) => "complete",
            _ => null
        };
    }

    // Gövdesi okunan uçlar: istek (olay JSON'u, senaryo, kullanıcı adı) ve cevaptan kısa özet.
    private static bool CapturesBody(string op) => op is "login" or "session" or "event" or "complete" or "resume";

    public async Task InvokeAsync(HttpContext ctx, Func<Task> next)
    {
        var op = Classify(ctx.Request.Method, ctx.Request.Path.Value ?? "");
        if (op is null || ctx.Request.Headers.ContainsKey(MonitorHeader))
        {
            await next();
            return;
        }

        string? requestBody = null;
        if (CapturesBody(op) && ctx.Request.ContentLength is > 0 and <= MaxBody * 4)
        {
            ctx.Request.EnableBuffering();
            requestBody = await new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
            ctx.Request.Body.Position = 0;
        }

        var original = ctx.Response.Body;
        MemoryStream? buffer = null;
        if (CapturesBody(op))
        {
            buffer = new MemoryStream();
            ctx.Response.Body = buffer;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await next();
        }
        finally
        {
            sw.Stop();
            string? responseBody = null;
            if (buffer is not null)
            {
                buffer.Position = 0;
                if (buffer.Length <= 256 * 1024)
                    responseBody = Encoding.UTF8.GetString(buffer.ToArray());
                buffer.Position = 0;
                await buffer.CopyToAsync(original);
                ctx.Response.Body = original;
            }
            try
            {
                Record(ctx, op, ctx.Response.StatusCode, sw.Elapsed.TotalMilliseconds, requestBody, responseBody);
            }
            catch (Exception ex)
            {
                // İzleme hiçbir zaman isteği bozmaz.
                _logger.LogWarning(ex, "VR izleme kaydı yazılamadı ({Op})", op);
            }
        }
    }

    private void Record(HttpContext ctx, string op, int status, double ms, string? requestBody, string? responseBody)
    {
        var path = ctx.Request.Path.Value ?? "";
        var detail = new Dictionary<string, object?>();
        string? sessionId = null;
        var seg = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length >= 3 && seg[1] == "sessions") sessionId = seg[2];

        JsonElement? req = Parse(requestBody);
        JsonElement? res = Parse(responseBody);
        string? loginUser = null, loginUserId = null;

        switch (op)
        {
            case "login":
                // Yalnız kullanıcı adı; şifre ve jeton okunmaz/tutulmaz.
                detail["username"] = Str(req, "username")?.Trim().ToLowerInvariant();
                if (status == 200)
                {
                    loginUser = Str(res, "user", "username");
                    loginUserId = Str(res, "user", "id");
                    detail["role"] = Str(res, "user", "role");
                    detail["expiresAt"] = Str(res, "expiresAt");
                }
                break;
            case "session":
                detail["scenarioId"] = Str(req, "scenarioId");
                detail["seed"] = Str(req, "seed");
                if (status == 200)
                {
                    sessionId = Str(res, "sessionId");
                    detail["scenarioName"] = Str(res, "scenarioName");
                    detail["score"] = Num(res, "score");
                    detail["nodeId"] = Str(res, "currentStep", "nodeId");
                    detail["stepTitle"] = Str(res, "currentStep", "title");
                    detail["surgeonRequest"] = Str(res, "currentStep", "surgeonRequest");
                    detail["kind"] = Str(res, "currentStep", "kind");
                }
                break;
            case "event":
                detail["request"] = requestBody is null ? null : Truncate(requestBody);
                detail["eventType"] = Str(req, "eventType");
                detail["eventId"] = Str(req, "eventId");
                detail["instrumentCode"] = Str(req, "instrumentCode");
                if (status == 200)
                {
                    var message = Str(res, "message");
                    detail["success"] = Bool(res, "success");
                    detail["score"] = Num(res, "score");
                    detail["message"] = message;
                    detail["duplicate"] = message?.Contains("yinelenen") == true;
                    detail["deviationType"] = Str(res, "deviation", "deviationType");
                    detail["expected"] = Str(res, "deviation", "expected");
                    detail["actual"] = Str(res, "deviation", "actual");
                    detail["nodeId"] = Str(res, "currentNodeId");
                    detail["nextNodeId"] = Str(res, "nextNodeId");
                    detail["nextTitle"] = Str(res, "nextStep", "title");
                    detail["completed"] = Bool(res, "completed");
                    detail["risk"] = Str(res, "complication", "possibleRisk");
                }
                break;
            case "complete":
            case "resume":
                detail["score"] = Num(res, "score");
                detail["successRate"] = Num(res, "successRate");
                break;
            case "asset":
                detail["file"] = seg.LastOrDefault();
                break;
        }
        if (status >= 400)
        {
            if (detail.GetValueOrDefault("message") is null) detail["message"] = Str(res, "message");
            detail["field"] = Str(res, "field");
        }

        var userId = loginUserId ?? ctx.User.UserId();
        var username = loginUser ?? ctx.User.Identity?.Name ?? ctx.User.FindFirst("unique_name")?.Value;
        var entry = new VrEntryDto
        {
            Op = op, Method = ctx.Request.Method, Path = path, Status = status, DurationMs = Math.Round(ms, 1),
            SessionId = sessionId, Detail = Compact(detail)
        };
        Add(Identify(ctx.Request.Headers, ctx.Connection.RemoteIpAddress), userId, username, entry, isLogin: loginUserId is not null);
    }

    // ---------- SignalR (SimulationHub) ----------

    public void RecordHub(HttpContext? http, string? userId, string? username, string op, bool ok, string? sessionId, string? message)
    {
        if (http is null || http.Request.Query.ContainsKey("monitor")) return;
        try
        {
            var who = Identify(http.Request.Headers, http.Connection.RemoteIpAddress);
            // SignalR istemcisinin User-Agent'ı REST istemcisinden farklı olabilir (Unity'de .NET SignalR):
            // aynı kullanıcı + aynı IP'deki en son cihaza yazılır.
            lock (_lock)
            {
                var same = _clients.Values.Select(c => c.Info)
                    .Where(c => c.UserId == userId && c.Ip == who.Ip && c.UserId is not null)
                    .OrderByDescending(c => c.LastSeen).FirstOrDefault();
                if (same is not null && who.Device is null)
                    who = who with { BaseKey = same.BaseKey, Label = same.Label, Kind = same.Kind };
            }
            Add(who, userId, username, new VrEntryDto
            {
                Op = op, Method = "HUB", Path = op == "hub-join" ? "JoinSession" : "/hubs/simulation",
                Status = ok ? 200 : 403, SessionId = sessionId,
                Detail = message is null ? null : new Dictionary<string, object?> { ["message"] = message }
            }, isLogin: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VR izleme: SignalR kaydı yazılamadı");
        }
    }

    // ---------- İzleme ekranı ----------

    public List<VrClientSnapshot> Snapshot(string? userId, bool instructor)
    {
        lock (_lock)
        {
            return _clients.Values
                .Where(c => instructor || (userId is not null && c.Info.UserId == userId))
                .OrderByDescending(c => c.Info.LastSeen)
                .Select(c => new VrClientSnapshot { Client = c.Info.Copy(), Entries = c.Entries.ToList() })
                .ToList();
        }
    }

    // ---------- iç ----------

    public sealed record Who(string BaseKey, string Label, string Kind, string? Ip, string? Device);

    public static Who Identify(IHeaderDictionary headers, IPAddress? remote)
    {
        var device = headers[DeviceHeader].ToString().Trim();
        if (device.Length > 60) device = device[..60];
        var ua = headers.UserAgent.ToString();
        var referer = headers.Referer.ToString();
        var ip = remote is null ? null : (remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString();

        var (label, kind) =
            device.Length > 0 ? (device, "vr")
            : ua.Contains("UnityPlayer", StringComparison.OrdinalIgnoreCase) ? ("Unity", "vr")
            : ua.Contains("OculusBrowser", StringComparison.OrdinalIgnoreCase) || ua.Contains("Quest", StringComparison.OrdinalIgnoreCase) ? ("Meta Quest tarayıcısı", "vr")
            : referer.Contains("/swagger", StringComparison.OrdinalIgnoreCase) ? ("Swagger", "tool")
            : ua.StartsWith("curl/", StringComparison.OrdinalIgnoreCase) ? ("curl", "tool")
            : ua.StartsWith("node", StringComparison.OrdinalIgnoreCase) || ua.Contains("undici", StringComparison.OrdinalIgnoreCase) ? ("Node betiği", "tool")
            : ua.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) ? ("Web tarayıcısı", "web")
            : ua.Length > 0 ? (ua.Split(' ', '/')[0], "tool")
            : ("Bilinmeyen istemci", "tool");

        // Aynı ağdaki (aynı IP) iki farklı program ayrı görünsün diye User-Agent de anahtarda.
        var baseKey = $"{label}|{(device.Length > 0 ? device : ua)}|{ip}";
        return new Who(baseKey, label, kind, ip, device.Length > 0 ? device : null);
    }

    private void Add(Who who, string? userId, string? username, VrEntryDto entry, bool isLogin)
    {
        VrClientDto info;
        VrClientSnapshot? merged = null;
        string? removedKey = null;
        lock (_lock)
        {
            if (userId is not null)
                _lastUser[who.BaseKey] = (userId, username ?? _lastUser.GetValueOrDefault(who.BaseKey).Username ?? "");
            else if (_lastUser.TryGetValue(who.BaseKey, out var last))
                (userId, username) = (last.UserId, last.Username);

            var key = $"{who.BaseKey}|{userId ?? "anon"}";
            if (!_clients.TryGetValue(key, out var client))
            {
                client = new Client { Info = new VrClientDto
                {
                    Key = key, BaseKey = who.BaseKey, Label = who.Label, Kind = who.Kind, Ip = who.Ip,
                    UserId = userId, Username = username, FirstSeen = DateTime.UtcNow
                } };
                _clients[key] = client;
                Evict();
            }

            // Girişten önceki girişsiz istekler (aynı cihaz) bu kullanıcıya taşınır: sihirbaz "sunucuya ulaştı" adımını da görsün.
            var anonKey = $"{who.BaseKey}|anon";
            if (isLogin && userId is not null && _clients.Remove(anonKey, out var anon))
            {
                foreach (var e in anon.Entries) { e.ClientKey = key; client.Entries.AddLast(e); }
                client.Info.FirstSeen = anon.Info.FirstSeen < client.Info.FirstSeen ? anon.Info.FirstSeen : client.Info.FirstSeen;
                removedKey = anonKey;
            }

            entry.Id = ++_nextId;
            entry.At = DateTime.UtcNow;
            entry.ClientKey = key;
            client.Entries.AddLast(entry);
            while (client.Entries.Count > MaxEntries) client.Entries.RemoveFirst();

            var c = client.Info;
            c.LastSeen = entry.At;
            c.RequestCount++;
            if (username is not null) c.Username = username;
            if (entry.SessionId is not null && entry.Status < 400) c.SessionId = entry.SessionId;
            if (entry.Detail?.GetValueOrDefault("score") is { } score) c.Score = Convert.ToInt32(score);
            if (entry.Op == "complete" && entry.Status == 200) c.Completed = true;
            if (entry.Op == "session" && entry.Status == 200) c.Completed = false;
            info = c.Copy();
            if (removedKey is not null)
                merged = new VrClientSnapshot { Client = info, Entries = client.Entries.ToList(), RemovedKey = removedKey };
        }

        _ = Broadcast(info, entry, merged);
    }

    private async Task Broadcast(VrClientDto client, VrEntryDto entry, VrClientSnapshot? merged)
    {
        try
        {
            var groups = client.UserId is null ? new[] { AllGroup } : new[] { AllGroup, UserGroup(client.UserId) };
            if (merged is not null)
                await _hub.Clients.Groups(groups).SendAsync("VrClient", merged);
            else
                await _hub.Clients.Groups(groups).SendAsync("VrActivity", new { client, entry });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "VR izleme yayını gönderilemedi");
        }
    }

    private void Evict()
    {
        if (_clients.Count <= MaxClients) return;
        foreach (var old in _clients.Values.OrderBy(c => c.Info.LastSeen).Take(_clients.Count - MaxClients).ToList())
            _clients.Remove(old.Info.Key);
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { return null; }
    }

    private static JsonElement? Get(JsonElement? root, params string[] path)
    {
        if (root is not { ValueKind: JsonValueKind.Object } e) return null;
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            var found = false;
            foreach (var p in e.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { e = p.Value; found = true; break; }
            if (!found) return null;
        }
        return e;
    }

    private static string? Str(JsonElement? root, params string[] path)
        => Get(root, path) is { } v ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToString(),
            _ => null
        } : null;

    private static double? Num(JsonElement? root, params string[] path)
        => Get(root, path) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    private static bool? Bool(JsonElement? root, params string[] path)
        => Get(root, path) is { } v && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static string Truncate(string s) => s.Length <= MaxBody ? s : s[..MaxBody] + "…";

    private static Dictionary<string, object?>? Compact(Dictionary<string, object?> d)
    {
        var kept = d.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        return kept.Count == 0 ? null : kept;
    }
}

public sealed class VrClientDto
{
    public string Key { get; set; } = "";
    public string BaseKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "tool";      // vr | web | tool
    public string? Ip { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? SessionId { get; set; }
    public int? Score { get; set; }
    public bool Completed { get; set; }
    public int RequestCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public VrClientDto Copy() => (VrClientDto)MemberwiseClone();
}

public sealed class VrEntryDto
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string ClientKey { get; set; } = "";
    public string Op { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int Status { get; set; }
    public double DurationMs { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, object?>? Detail { get; set; }
}

public sealed class VrClientSnapshot
{
    public VrClientDto Client { get; set; } = new();
    public List<VrEntryDto> Entries { get; set; } = new();
    public string? RemovedKey { get; set; }
}
