using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Arqenion.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Arqenion.Api.Tests;

// Kimlik doğrulama ve yetki: giriş, sahiplik, rol, pasif kullanıcı, kaba kuvvet sınırı.
public sealed class AuthTests : IClassFixture<VrReplayTests.Api>
{
    private readonly VrReplayTests.Api _api;
    public AuthTests(VrReplayTests.Api api) => _api = api;

    [Theory]
    [InlineData("POST", "/api/sessions")]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/issues")]
    [InlineData("POST", "/api/issues")]
    public async Task Protected_endpoints_require_a_token(string method, string path)
    {
        var res = await _api.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/procedure")]
    [InlineData("/sim/index.html")]
    public async Task Public_endpoints_stay_open(string path)
        => Assert.Equal(HttpStatusCode.OK, (await _api.CreateClient().GetAsync(path)).StatusCode);

    [Fact]
    public async Task Readiness_reports_degraded_without_ai_and_lists_no_secrets()
    {
        var res = await _api.CreateClient().GetAsync("/api/health/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!;
        Assert.Equal("degraded", json["status"]!.GetValue<string>());          // testte AI kapalı; karar yine çalışır
        Assert.True(json["checks"]!["database"]!["ok"]!.GetValue<bool>());
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_list_is_scoped_to_the_user()
    {
        var can = await _api.ClientAsAsync("can");
        var ece = await _api.ClientAsAsync("ece");
        await can.PostAsJsonAsync("/api/sessions", new { scenarioId = "case-basic-01" });
        var mine = JsonNode.Parse(await can.GetStringAsync("/api/sessions"))!.AsArray();
        Assert.NotEmpty(mine);
        Assert.All(mine, s => Assert.Equal("can", s!["username"]!.GetValue<string>()));
        Assert.Empty(JsonNode.Parse(await ece.GetStringAsync("/api/sessions"))!.AsArray()
            .Where(s => s!["username"]!.GetValue<string>() == "can"));
        var all = JsonNode.Parse(await (await _api.ClientAsAsync()).GetStringAsync("/api/sessions"))!.AsArray();
        Assert.Contains(all, s => s!["username"]!.GetValue<string>() == "can");
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_answer()
    {
        var http = _api.CreateClient();
        var wrong = await http.PostAsJsonAsync("/api/auth/login", new { username = VrReplayTests.Api.AdminUser, password = "yanlis-sifre-123" });
        var unknown = await http.PostAsJsonAsync("/api/auth/login", new { username = "olmayan-kullanici", password = "yanlis-sifre-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_trainee_cannot_see_another_trainees_session_but_the_instructor_can()
    {
        var ayse = await _api.ClientAsAsync("ayse");
        var mehmet = await _api.ClientAsAsync("mehmet");
        var instructor = await _api.ClientAsAsync();
        var start = JsonNode.Parse(await (await ayse.PostAsJsonAsync("/api/sessions", new { scenarioId = "case-basic-01", userId = "mehmet-id-taklidi" })).Content.ReadAsStringAsync())!;
        var sid = start["sessionId"]!.GetValue<string>();

        foreach (var path in new[] { $"/api/sessions/{sid}", $"/api/sessions/{sid}/flow", $"/api/sessions/{sid}/events" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await mehmet.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ayse.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await instructor.GetAsync(path)).StatusCode);
        }
        var ev = await mehmet.PostAsJsonAsync($"/api/sessions/{sid}/events", new { eventId = "m1", eventType = "step_confirmed" });
        Assert.Equal(HttpStatusCode.NotFound, ev.StatusCode);
    }

    [Fact]
    public async Task Only_instructors_manage_users_and_read_issues()
    {
        var trainee = await _api.ClientAsAsync("zeynep");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await trainee.PostAsJsonAsync("/api/auth/users", new { username = "yeni", password = "uzun-bir-sifre", role = "instructor" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await trainee.GetAsync("/api/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await trainee.PostAsJsonAsync("/api/issues", new { body = "tepsi kaydı yavaş" })).StatusCode);

        var instructor = await _api.ClientAsAsync();
        Assert.Equal(HttpStatusCode.OK, (await instructor.GetAsync("/api/issues")).StatusCode);
        var shortPw = await instructor.PostAsJsonAsync("/api/auth/users", new { username = "kisa", password = "123", role = "trainee" });
        Assert.Equal(HttpStatusCode.BadRequest, shortPw.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_user_loses_access_immediately()
    {
        var trainee = await _api.ClientAsAsync("ali");
        var me = JsonNode.Parse(await trainee.GetStringAsync("/api/auth/me"))!;
        var instructor = await _api.ClientAsAsync();
        (await instructor.PostAsync($"/api/auth/users/{me["id"]}/active?value=false", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await trainee.GetAsync("/api/auth/me")).StatusCode);
        (await instructor.PostAsync($"/api/auth/users/{me["id"]}/active?value=true", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_are_rejected()
    {
        var other = new AuthService(new AuthOptions
        {
            SigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(new string('k', 48)))
        });
        var (token, _) = other.IssueToken(new Models.User { Username = "sahte", Role = Models.Roles.Instructor });
        var http = _api.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/issues")).StatusCode);
    }

    [Fact]
    public void Passwords_are_hashed_with_salt()
    {
        var a = AuthService.HashPassword("ayni-sifre-123");
        var b = AuthService.HashPassword("ayni-sifre-123");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("ayni-sifre", a);
        Assert.True(AuthService.VerifyPassword("ayni-sifre-123", a));
        Assert.False(AuthService.VerifyPassword("ayni-sifre-124", a));
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        await using var strict = _api.WithWebHostBuilder(b => b.UseSetting("Auth:LoginPerMinute", "3"));
        var http = strict.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await http.PostAsJsonAsync("/api/auth/login", new { username = "x", password = "yanlis-sifre-123" })).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
    }
}
