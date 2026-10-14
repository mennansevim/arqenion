using System.Text.Json;
using Arqenion.Api.Models;
using Arqenion.Api.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arqenion.Api.Tests;

// Testlerin ortak kurulumu: depodaki gerçek model dosyaları (procedure-model/dist, backend/Procedures).
public static class TestModel
{
    public static readonly string Repo = FindRepo();
    public static readonly string Dist = Path.Combine(Repo, "procedure-model", "dist");
    public static readonly string Backend = Path.Combine(Repo, "backend");

    public static readonly FlowModelStore Model =
        new(Dist, "2.4", Path.Combine(Backend, "Procedures", "scoring.json"), Path.Combine(Backend, "Procedures", "instrument-descriptors.json"));

    public static readonly ProcedureStore Store = new(new Env(Backend), NullLogger<ProcedureStore>.Instance);

    public static FlowProcedureService Service()
        => new(Model, new FlowEngine(Model), new ScoringService(Store), new FlowClinicalText(Model, Store));

    public static FlowClinicalText Text() => new(Model, Store);

    public static (SessionRecord Session, FlowProcedureService Service) StartSession(string scenarioId)
    {
        var scenario = Store.GetScenario(scenarioId) ?? throw new ArgumentException(scenarioId);
        var session = new SessionRecord { ScenarioId = scenarioId, Score = 100, RawScore = 100 };
        var service = Service();
        service.Start(session, scenario);
        return (session, service);
    }

    // Bulunulan düğümde doğru olan olay(lar): motorun kendi beklentisinden üretilir.
    public static IEnumerable<EventRequest> CorrectEvents(SessionRecord session)
    {
        var state = FlowProcedureService.Load(session);
        var x = Model.Expect(session.CurrentNodeId!, state.Parameters);
        var (deliverable, sutures, _) = Service().Allowed(session.CurrentNodeId!, x, state);
        switch (x.Kind)
        {
            case "confirm":
                yield return new EventRequest { EventType = "step_confirmed" };
                yield break;
            case "count":
                var c = x.CountCategory!;
                yield return new EventRequest { EventType = "count_confirmed", Counts = new() { [c] = state.Counts[c] } };
                yield break;
        }
        if (x.Clamps.Count > 0)
        {
            // Daha önce teslim edilenler düşülür: yalnızca kalan klempler verilir.
            foreach (var clamp in x.Clamps)
                for (var i = state.Delivered.GetValueOrDefault(clamp.Code); i < clamp.Qty; i++)
                    yield return Deliver(clamp.Code);
            yield break;
        }
        yield return Deliver(deliverable.FirstOrDefault(), sutures.FirstOrDefault());
    }

    public static EventRequest Deliver(string? instrument, string? suture = null)
        => new() { EventType = "instrument_delivered", InstrumentCode = instrument, SutureCode = suture, Target = "SURGEON" };

    // Senaryoyu baştan sona doğru oynar; yürünen düğüm yolunu döner.
    public static List<string> PlayCorrectly(SessionRecord session, FlowProcedureService service, Action<ValidationOutcome>? check = null)
    {
        var guard = 0;
        while (session.CurrentNodeId is not null)
        {
            Assert.True(++guard < 500, "akış bitmedi");
            foreach (var ev in CorrectEvents(session).ToList())
            {
                var o = service.Validate(session, ev);
                Assert.True(o.Success, $"{o.CurrentNodeId}: {o.Message} {o.Deviation?.DeviationType}");
                check?.Invoke(o);
            }
        }
        return FlowProcedureService.Load(session).Path;
    }

    public static Dictionary<string, JsonElement> Params(params (string Key, object Value)[] items)
        => items.ToDictionary(i => i.Key, i => JsonSerializer.SerializeToElement(i.Value));

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "procedure-model")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Depo kökü bulunamadı");
    }

    private sealed class Env : IHostEnvironment
    {
        public Env(string root)
        {
            ContentRootPath = root;
            ContentRootFileProvider = new PhysicalFileProvider(root);
        }

        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Arqenion.Api";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
