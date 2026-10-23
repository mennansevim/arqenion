using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arqenion.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Arqenion.Api.Tests;

// VR bağlantı testi (simulator/vr.html): sözleşme uçlarına gelen istekler cihaz başına kaydedilir ve yayılır.
// Şifre/jeton tutulmaz; izleme ekranının kendi istekleri sayılmaz; kursiyer yalnız kendi cihazını görür.
public sealed class VrMonitorTests : IClassFixture<ScoreUpdateTests.Api>
{
    private readonly ScoreUpdateTests.Api _api;
    public VrMonitorTests(ScoreUpdateTests.Api api) => _api = api;

    private VrMonitor Monitor => _api.Services.GetRequiredService<VrMonitor>();

    private HttpClient Device(string name)
    {
        var http = _api.CreateClient();
        http.DefaultRequestHeaders.Add(VrMonitor.DeviceHeader, name);
        return http;
    }

    private async Task<string> AdminTokenAsync()
        => await VrReplayTests.Api.LoginAsync(_api.CreateClient(), ScoreUpdateTests.Api.AdminUser, ScoreUpdateTests.Api.AdminPassword);

    private async Task<(string Username, string Password)> TraineeAsync(string username)
    {
        var password = "test-sifre-" + username;
        var admin = _api.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", await AdminTokenAsync());
        await admin.PostAsJsonAsync("/api/auth/users", new { username, password, role = "trainee" });
        return (username, password);
    }

    private async Task<string> LoginAsync(HttpClient http, string username, string password)
    {
        var token = await VrReplayTests.Api.LoginAsync(http, username, password);
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return token;
    }

    private VrClientSnapshot ClientOf(string device) =>
        Monitor.Snapshot(null, instructor: true).Single(c => c.Client.Label == device);

    [Fact]
    public async Task Device_steps_are_recorded_in_order_without_password_or_token()
    {
        var (user, pass) = await TraineeAsync("vr-izleme-1");
        var quest = Device("Quest 3 · izleme-1");
        Assert.Equal(HttpStatusCode.OK, (await quest.GetAsync("/api/health")).StatusCode);
        var token = await LoginAsync(quest, user, pass);
        await quest.GetAsync("/api/procedure");
        await quest.GetAsync("/api/assets/manifest");
        var start = JsonNode.Parse(await (await quest.PostAsJsonAsync("/api/sessions",
            new { scenarioId = "case-basic-01", seed = "ilk-test" })).Content.ReadAsStringAsync())!;
        var sid = start["sessionId"]!.GetValue<string>();
        var ok = await quest.PostAsJsonAsync($"/api/sessions/{sid}/events",
            new { eventId = "izleme-1", eventType = "instrument_delivered", instrumentCode = "SCALPEL_BLADE_10", target = "SURGEON" });
        Assert.True(JsonNode.Parse(await ok.Content.ReadAsStringAsync())!["success"]!.GetValue<bool>());   // cevap bozulmadı
        await quest.PostAsJsonAsync($"/api/sessions/{sid}/events", new { eventId = "izleme-1", eventType = "instrument_delivered", instrumentCode = "SCALPEL_BLADE_10" });
        await quest.PostAsJsonAsync($"/api/sessions/{sid}/events", new { eventId = "izleme-2", eventType = "instrument_give", instrumentCode = "SCALPEL_BLADE_10" });
        await quest.PostAsync($"/api/sessions/{sid}/complete", null);

        var c = ClientOf("Quest 3 · izleme-1");
        Assert.Equal("vr", c.Client.Kind);
        Assert.Equal(user, c.Client.Username);
        Assert.Equal(sid, c.Client.SessionId);
        Assert.True(c.Client.Completed);
        // Girişten önceki sağlık isteği de aynı cihaza (kullanıcıya) taşındı.
        Assert.Equal(new[] { "health", "login", "procedure", "manifest", "session", "event", "event", "event", "complete" },
            c.Entries.Select(e => e.Op));

        var login = c.Entries[1];
        Assert.Equal(200, login.Status);
        Assert.Equal(user, login.Detail!["username"]);
        var json = JsonSerializer.Serialize(c);
        Assert.DoesNotContain(pass, json);
        Assert.DoesNotContain(token, json);

        var session = c.Entries[4].Detail!;
        Assert.Equal("case-basic-01", session["scenarioId"]);
        Assert.Equal("No:10 bistüri ucu", session["surgeonRequest"]);

        var (first, dup, bad) = (c.Entries[5].Detail!, c.Entries[6].Detail!, c.Entries[7]);
        Assert.Equal(true, first["success"]);
        Assert.Equal("2", first["nextNodeId"]);
        Assert.Contains("SCALPEL_BLADE_10", (string)first["request"]!);
        Assert.Equal(true, dup["duplicate"]);
        Assert.Equal(400, bad.Status);
        Assert.Equal("eventType", bad.Detail!["field"]);
    }

    [Fact]
    public async Task Wrong_instrument_and_failed_login_are_recorded()
    {
        var (user, pass) = await TraineeAsync("vr-izleme-2");
        var quest = Device("Quest 3 · izleme-2");
        var denied = await quest.PostAsJsonAsync("/api/auth/login", new { username = user, password = "yanlis-sifre-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await LoginAsync(quest, user, pass);
        var start = JsonNode.Parse(await (await quest.PostAsJsonAsync("/api/sessions",
            new { scenarioId = "case-basic-01", seed = "ilk-test" })).Content.ReadAsStringAsync())!;
        await quest.PostAsJsonAsync($"/api/sessions/{start["sessionId"]}/events",
            new { eventId = "izleme-3", eventType = "instrument_delivered", instrumentCode = "SCALPEL_BLADE_15", target = "SURGEON" });

        var c = ClientOf("Quest 3 · izleme-2");
        var fail = c.Entries.First(e => e.Op == "login");
        Assert.Equal(401, fail.Status);
        Assert.DoesNotContain("yanlis-sifre-123", JsonSerializer.Serialize(c));
        var wrong = c.Entries.Last(e => e.Op == "event").Detail!;
        Assert.Equal(false, wrong["success"]);
        Assert.Equal("WRONG_INSTRUMENT", wrong["deviationType"]);
        Assert.Equal("SCALPEL_BLADE_10", wrong["expected"]);
        Assert.Equal(92.0, wrong["score"]);
        Assert.Equal(92, c.Client.Score);
    }

    [Fact]
    public async Task Monitor_page_requests_are_not_recorded_and_activity_is_broadcast()
    {
        var page = Device("İzleme ekranı");
        page.DefaultRequestHeaders.Add(VrMonitor.MonitorHeader, "1");
        await page.GetAsync("/api/health");
        Assert.DoesNotContain(Monitor.Snapshot(null, true), c => c.Client.Label == "İzleme ekranı");

        var before = _api.Hub.Sent.Count(m => m.Method == "VrActivity");
        await Device("Quest 3 · yayın").GetAsync("/api/procedure");
        Assert.True(_api.Hub.Sent.Count(m => m.Method == "VrActivity") > before);
    }

    [Fact]
    public async Task Trainee_sees_only_own_devices_instructor_sees_all()
    {
        var (a, pa) = await TraineeAsync("vr-izleme-a");
        var (b, pb) = await TraineeAsync("vr-izleme-b");
        var qa = Device("Quest · a");
        var qb = Device("Quest · b");
        await LoginAsync(qa, a, pa);
        await LoginAsync(qb, b, pb);
        await qa.GetAsync("/api/procedure");
        await qb.GetAsync("/api/procedure");

        var idA = ClientOf("Quest · a").Client.UserId!;
        var own = Monitor.Snapshot(idA, instructor: false);
        Assert.Contains(own, c => c.Client.Label == "Quest · a");
        Assert.DoesNotContain(own, c => c.Client.Label == "Quest · b");
        Assert.All(own, c => Assert.Equal(idA, c.Client.UserId));

        var all = Monitor.Snapshot(null, instructor: true);
        Assert.Contains(all, c => c.Client.Label == "Quest · a");
        Assert.Contains(all, c => c.Client.Label == "Quest · b");
    }

    [Theory]
    [InlineData("UnityPlayer/2022.3.20f1 (UnityWebRequest/1.0, libcurl/8.5.0-DEV)", "Unity", "vr")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; Quest 3) AppleWebKit/537.36 OculusBrowser/33.0", "Meta Quest tarayıcısı", "vr")]
    [InlineData("curl/8.7.1", "curl", "tool")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15", "Web tarayıcısı", "web")]
    public void Client_kind_is_read_from_user_agent(string ua, string label, string kind)
    {
        var headers = new Microsoft.AspNetCore.Http.HeaderDictionary { ["User-Agent"] = ua };
        var who = VrMonitor.Identify(headers, IPAddress.Loopback);
        Assert.Equal(label, who.Label);
        Assert.Equal(kind, who.Kind);
    }
}
