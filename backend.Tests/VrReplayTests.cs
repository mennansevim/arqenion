using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Arqenion.Api.Tests;

// VR'ın göndereceği JSON olayları gerçek HTTP uçlarına (süreç içi sunucu) birebir gönderir ve her cevabı kayıtla
// karşılaştırır. Kayıtlar: VrSessions/*.json (scripts/vr-replay.mjs record ile üretilir; sözleşme: docs/vr-events.md).
public sealed class VrReplayTests : IClassFixture<VrReplayTests.Api>
{
    public sealed class Api : WebApplicationFactory<Program>
    {
        // Yalnız testte kullanılan değerler; gerçek kurulumdaki anahtar ve şifrelerle ilgisi yok.
        public const string AdminUser = "test-egitmen";
        public static readonly string AdminPassword = "test-" + Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Procedure:Model", "v2.4");
            builder.UseSetting("AiGateway:BaseUrl", "");   // AI çağrısı yok; karar ve klinik kart AI'dan bağımsız
            builder.UseSetting("Auth:SigningKey", "test-imza-anahtari-" + new string('x', 40));
            builder.UseSetting("Auth:BootstrapAdmin:Username", AdminUser);
            builder.UseSetting("Auth:BootstrapAdmin:Password", AdminPassword);
            builder.UseSetting("Auth:LoginPerMinute", "1000");
        }

        // Girişi yapılmış istemci: eğitmen ya da (yoksa açılan) kursiyer.
        public async Task<HttpClient> ClientAsAsync(string? username = null, string role = "trainee")
        {
            var http = CreateClient();
            var admin = await LoginAsync(http, AdminUser, AdminPassword);
            if (username is null) return Bearer(http, admin);
            var password = "test-sifre-" + username;
            var create = new HttpRequestMessage(HttpMethod.Post, "/api/auth/users")
                { Content = JsonContent.Create(new { username, password, role }) };
            create.Headers.Authorization = new("Bearer", admin);
            await http.SendAsync(create);   // varsa 409, sorun değil
            return Bearer(CreateClient(), await LoginAsync(http, username, password));
        }

        public static async Task<string> LoginAsync(HttpClient http, string username, string password)
        {
            var res = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
            res.EnsureSuccessStatusCode();
            return JsonNode.Parse(await res.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
        }

        private static HttpClient Bearer(HttpClient http, string token)
        {
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            return http;
        }
    }

    private static readonly string Dir = Path.Combine(TestModel.Repo, "backend.Tests", "VrSessions");
    private readonly Api _api;
    public VrReplayTests(Api api) => _api = api;

    public static IEnumerable<object[]> Sessions()
        => Directory.GetFiles(Dir, "*.json").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Theory]
    [MemberData(nameof(Sessions))]
    public async Task Recorded_vr_session_replays_identically(string name)
    {
        var rec = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Dir, name + ".json")))!;
        var http = await _api.ClientAsAsync("vr-kursiyer");
        var start = await http.PostAsJsonAsync("/api/sessions", rec["start"]);
        start.EnsureSuccessStatusCode();
        var sid = JsonNode.Parse(await start.Content.ReadAsStringAsync())!["sessionId"]!.GetValue<string>();

        var i = 0;
        foreach (var e in rec["events"]!.AsArray())
        {
            i++;
            var res = await http.PostAsync($"/api/sessions/{sid}/events",
                new StringContent(e!["send"]!.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
            var got = Summarize((int)res.StatusCode, JsonNode.Parse(await res.Content.ReadAsStringAsync()) ?? new JsonObject());
            var expected = e["expect"]!.ToJsonString();
            Assert.True(got == expected, $"{name} #{i} {e["send"]!["eventType"]}\n  beklenen {expected}\n  gelen    {got}");
        }

        var report = JsonNode.Parse(await (await http.PostAsync($"/api/sessions/{sid}/complete", null)).Content.ReadAsStringAsync())!;
        foreach (var (key, value) in rec["report"]!.AsObject())
            Assert.Equal(value!.ToJsonString(), report[key]!.ToJsonString());
    }

    [Fact]
    public async Task Contract_errors_do_not_touch_score_or_records()
    {
        var http = await _api.ClientAsAsync("vr-kursiyer");
        var start = JsonNode.Parse(await (await http.PostAsJsonAsync("/api/sessions", new { scenarioId = "case-basic-01" })).Content.ReadAsStringAsync())!;
        var sid = start["sessionId"]!.GetValue<string>();
        var bad = await http.PostAsJsonAsync($"/api/sessions/{sid}/events", new { eventId = "x1", eventType = "instrument_give" });
        Assert.Equal(400, (int)bad.StatusCode);
        var body = JsonNode.Parse(await bad.Content.ReadAsStringAsync())!;
        Assert.Equal("eventType", body["field"]!.GetValue<string>());
        Assert.Contains("instrument_delivered", body["allowed"]!.AsArray().Select(a => a!.GetValue<string>()));

        var events = JsonNode.Parse(await http.GetStringAsync($"/api/sessions/{sid}/events"))!.AsArray();
        Assert.Empty(events);
        var flow = JsonNode.Parse(await http.GetStringAsync($"/api/sessions/{sid}/flow"))!;
        Assert.Equal("1", flow["currentNodeId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Simulator_page_is_revalidated_so_users_get_the_new_version()
    {
        var res = await _api.CreateClient().GetAsync("/sim/index.html");
        res.EnsureSuccessStatusCode();
        Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());
    }

    // scripts/vr-replay.mjs summarize() ile aynı özet ve alan sırası.
    private static string Summarize(int status, JsonNode b)
    {
        var o = new JsonObject { ["status"] = status };
        if (status != 200)
        {
            o["field"] = b["field"]?.DeepClone();
            return o.ToJsonString();
        }
        o["success"] = b["success"]!.DeepClone();
        o["currentNodeId"] = b["currentNodeId"]?.DeepClone();
        o["nextNodeId"] = b["nextNodeId"]?.DeepClone();
        o["deviationType"] = b["deviation"]?["deviationType"]?.DeepClone();
        o["score"] = b["score"]!.DeepClone();
        var branch = b["branch"];
        o["branch"] = branch is null ? null : $"{branch["condition"]}{(branch["resolved"]!.GetValue<bool>() ? ":resolved" : "")}";
        o["adaptive"] = new JsonArray((b["adaptive"]?.AsArray() ?? new JsonArray())
            .Select(a => a!["condition"]!.GetValue<string>()).Order(StringComparer.Ordinal).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray());
        o["completed"] = b["completed"]?.GetValue<bool>() ?? false;
        return o.ToJsonString();
    }
}
