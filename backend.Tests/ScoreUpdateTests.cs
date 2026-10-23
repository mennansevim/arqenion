using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Arqenion.Api.Hubs;
using Arqenion.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Arqenion.Api.Tests;

// ScoreUpdate yalnız puan değişince gönderilir (docs/vr-events.md); doğru adım ve yinelenen olay mesaj üretmez.
public sealed class ScoreUpdateTests : IClassFixture<ScoreUpdateTests.Api>
{
    public sealed class Api : WebApplicationFactory<Program>
    {
        // Bellek içi veritabanı test fabrikaları arasında ortak; ilk eğitmen VrReplayTests ile aynı olmalı.
        public const string AdminUser = VrReplayTests.Api.AdminUser;
        public static readonly string AdminPassword = VrReplayTests.Api.AdminPassword;
        public readonly RecordingHub Hub = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Procedure:Model", "v2.4");
            builder.UseSetting("AiGateway:BaseUrl", "");
            builder.UseSetting("Auth:SigningKey", "test-imza-anahtari-" + new string('x', 40));
            builder.UseSetting("Auth:BootstrapAdmin:Username", AdminUser);
            builder.UseSetting("Auth:BootstrapAdmin:Password", AdminPassword);
            builder.UseSetting("Auth:LoginPerMinute", "1000");
            builder.ConfigureTestServices(s => s.AddSingleton<IHubContext<SimulationHub>>(Hub));
        }
    }

    // Gönderilen hub mesajlarını (hedef, grup, ad, ilk argüman) biriktirir.
    public sealed class RecordingHub : IHubContext<SimulationHub>, IHubClients, IClientProxy
    {
        public readonly ConcurrentQueue<(string Group, string Method, object? Arg)> Sent = new();
        private readonly AsyncLocal<string> _group = new();

        public IHubClients Clients => this;
        public IGroupManager Groups => throw new NotSupportedException();
        public IClientProxy Group(string groupName) { _group.Value = groupName; return this; }
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Enqueue((_group.Value ?? "", method, args.FirstOrDefault()));
            return Task.CompletedTask;
        }
    }

    private readonly Api _api;
    public ScoreUpdateTests(Api api) => _api = api;

    [Fact]
    public async Task Score_update_is_sent_only_when_score_changes()
    {
        var http = _api.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", await VrReplayTests.Api.LoginAsync(http, Api.AdminUser, Api.AdminPassword));
        var start = JsonNode.Parse(await (await http.PostAsJsonAsync("/api/sessions",
            new { scenarioId = "case-basic-01", seed = "score-update" })).Content.ReadAsStringAsync())!;
        var sid = start["sessionId"]!.GetValue<string>();
        var correct = start["currentStep"]!["allowedInstruments"]![0]!.GetValue<string>();
        int Updates() => _api.Hub.Sent.Count(m => m.Group == sid && m.Method == "ScoreUpdate");

        async Task<JsonNode> Send(string eventId, string instrument)
        {
            var res = await http.PostAsJsonAsync($"/api/sessions/{sid}/events",
                new { eventId, eventType = "instrument_delivered", instrumentCode = instrument, target = "SURGEON" });
            res.EnsureSuccessStatusCode();
            return JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        }

        var wrong = await Send("su-1", "SCALPEL_BLADE_11");
        Assert.False(wrong["success"]!.GetValue<bool>());
        Assert.Equal(1, Updates());
        var update = (ScoreUpdateDto)_api.Hub.Sent.Last(m => m.Group == sid && m.Method == "ScoreUpdate").Arg!;
        Assert.Equal(wrong["score"]!.GetValue<int>(), update.Score);

        await Send("su-1", "SCALPEL_BLADE_11");   // yinelenen olay: puan değişmez
        Assert.Equal(1, Updates());

        var ok = await Send("su-2", correct);      // doğru adım: kesinti modelinde puan değişmez
        Assert.True(ok["success"]!.GetValue<bool>());
        Assert.Equal(1, Updates());
    }
}
