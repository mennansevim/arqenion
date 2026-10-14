using System.Text.Json;
using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Tests;

// Yük/fuzz testleri: rastgele koşul planı + hasta parametresi + rastgele (çoğu hatalı) olay akışı.
// Amaç: motorun her durumda Python motoruyla aynı kararı vermesi, çökmemesi ve yanlışı/doğruyu karıştırmaması.
public sealed class FlowStressTests
{
    private static readonly JsonElement Random400 =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(TestModel.Dist, "open-appendectomy-v2.4.golden-random.json"))).RootElement;

    private static readonly string[] EventTypes =
    {
        "instrument_delivered", "instrument_placed", "step_confirmed", "count_confirmed", "hint_requested",
        "sterile_area_violated", "instrument_dropped", "bogus_event", ""
    };

    private static readonly HashSet<string> DeviationTypes = new()
    {
        "WRONG_INSTRUMENT", "WRONG_ORDER", "STERILE_VIOLATION", "COUNT_MISMATCH", "INSTRUMENT_DROPPED"
    };

    private static readonly HashSet<string> Severities = new() { "LOW", "MEDIUM", "HIGH", "CRITICAL" };

    public static IEnumerable<object[]> RandomCases()
        => Random400.GetProperty("cases").EnumerateArray().Select((c, i) => new object[] { i });

    private static (Dictionary<string, List<bool>> Conditions, Dictionary<string, JsonElement> Parameters, List<string> Path, JsonElement Expected)
        Case(int i)
    {
        var c = Random400.GetProperty("cases")[i];
        return (
            c.GetProperty("conditions").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetBoolean()).ToList()),
            c.GetProperty("parameters").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
            c.GetProperty("path").EnumerateArray().Select(x => x.GetString()!).ToList(),
            c.GetProperty("expectedInstruments"));
    }

    // 1) Motor: 400 rastgele vakada Python ile aynı yol ve hastaya bağlı adımlarda aynı aletler.
    [Theory]
    [MemberData(nameof(RandomCases))]
    public void Engine_matches_python_on_random_plans(int i)
    {
        var (conditions, parameters, path, expected) = Case(i);
        var engine = new FlowEngine(TestModel.Model);
        var state = engine.Start(conditions, parameters);
        for (var node = state.Path[0]; node is not null; node = engine.Advance(state, node).Next)
        {
            Assert.True(state.Path.Count < 200);
            if (!expected.TryGetProperty(node, out var g)) continue;
            var x = TestModel.Model.Expect(node, state.Parameters);
            Assert.Equal(g.GetProperty("accepted").EnumerateArray().Select(a => a.GetString()!), x.Accepted);
            var clamps = g.TryGetProperty("clamps", out var cl)
                ? cl.EnumerateArray().Select(a => (a.GetProperty("code").GetString()!, a.GetProperty("qty").GetInt32())).ToList()
                : new();
            Assert.Equal(clamps, x.Clamps.Select(c => (c.Code, c.Qty)).ToList());
            Assert.Equal(g.TryGetProperty("byParameter", out var bp) ? bp.GetProperty("value").GetString() : null, x.ByParameter?.Value);
        }
        Assert.Equal(path, state.Path);
    }

    // 2) Servis + gürültü: araya giren her türlü hatalı olay kararı ve yolu değiştirmez; değişmezler korunur.
    [Theory]
    [MemberData(nameof(RandomCases))]
    public void Noisy_session_keeps_invariants_and_follows_the_same_path(int i)
    {
        var (conditions, parameters, path, _) = Case(i);
        var rnd = new Random(i);
        var service = TestModel.Service();
        var scenario = new Scenario
        {
            ScenarioId = $"random-{i}",
            Patient = new PatientProfile { BodyType = parameters["PATIENT_BODY_TYPE"].GetString()! },
            Conditions = conditions.ToDictionary(c => c.Key, c => JsonSerializer.SerializeToElement(c.Value)),
            Parameters = parameters.Where(p => p.Key != "PATIENT_BODY_TYPE").ToDictionary(p => p.Key, p => p.Value),
            Counts = new() { ["COMPRESS"] = rnd.Next(1, 15), ["INSTRUMENT"] = rnd.Next(5, 40), ["NEEDLE"] = rnd.Next(1, 6) },
            MissingItem = new[] { "COMPRESS", "INSTRUMENT", "NEEDLE" }[rnd.Next(3)]
        };
        var session = new SessionRecord { Score = 100, RawScore = 100 };
        service.Start(session, scenario);

        var codes = TestModel.Model.Catalog.Keys.Append("UNKNOWN_CODE").ToArray();
        var correctCount = 0;
        for (var guard = 0; session.CurrentNodeId is not null; guard++)
        {
            Assert.True(guard < 3000, "akış bitmedi");
            var before = session.CurrentNodeId;
            var state = FlowProcedureService.Load(session);
            var x = TestModel.Model.Expect(before, state.Parameters);
            var triggeredBefore = session.ComplicationsTriggered;

            if (rnd.NextDouble() < 0.6)
            {
                var ev = RandomEvent(rnd, codes, x);
                var verdict = Oracle(x, state, ev);
                var o = service.Validate(session, ev);
                AssertInvariants(session, o);
                if (o.Deviation is not null)
                {
                    Assert.Equal(before, session.CurrentNodeId);
                    Assert.Equal(triggeredBefore, session.ComplicationsTriggered);
                    Assert.Contains(o.Deviation.DeviationType, DeviationTypes);
                    Assert.Contains(o.Deviation.Severity, Severities);
                    Assert.False(string.IsNullOrWhiteSpace(o.Message));
                    var text = TestModel.Text().Entry(before, o.Deviation.DeviationType, o.Deviation.Actual, x.Deliverable);
                    Assert.False(string.IsNullOrWhiteSpace(text.PossibleRisk));
                    Assert.False(string.IsNullOrWhiteSpace(text.Explanation));
                }
                if (o.Success && o.Deviation is null && ev.EventType != "hint_requested") correctCount++;
                if (verdict is { } accept)
                    Assert.True(accept == (o.Deviation is null),
                        $"{before}: {ev.EventType} {ev.InstrumentCode}/{ev.SutureCode} {JsonSerializer.Serialize(ev.Counts)} → beklenen {(accept ? "kabul" : "ret")}, motor: {o.Message}");
                continue;
            }

            foreach (var ev in TestModel.CorrectEvents(session).ToList())
            {
                var o = service.Validate(session, ev);
                AssertInvariants(session, o);
                Assert.True(o.Success, $"{before}: doğru olay reddedildi: {o.Message}");
                correctCount++;
            }
        }

        Assert.Equal(path, FlowProcedureService.Load(session).Path);
        Assert.Equal(session.ComplicationsTriggered, session.ComplicationsResolved);
        Assert.True(correctCount >= path.Count, $"doğru olay {correctCount} < yol {path.Count}");
    }

    // 3) Uzun soak: C# tarafında üretilen rastgele planlar (ARQ_SOAK=5000 ile çalışır; varsayılan 300).
    //    Gürültülü seansın yolu, aynı planla gürültüsüz yürünen motor yoluyla aynı olmalı.
    [Fact]
    public void Soak_random_plans_with_heavy_noise()
    {
        var count = int.TryParse(Environment.GetEnvironmentVariable("ARQ_SOAK"), out var n) ? n : 300;
        var model = TestModel.Model;
        var conds = model.Graph.Conditions.Where(c => c.Value.Source != "OUTCOME").Select(c => c.Key).ToArray();
        var choices = new List<bool>?[] { null, new() { true }, new() { false }, new() { true, false }, new() { true, true }, new() { false, true } };
        var codes = model.Catalog.Keys.Append("UNKNOWN_CODE").ToArray();
        for (var seed = 1000; seed < 1000 + count; seed++)
        {
            var rnd = new Random(seed);
            var plan = new Dictionary<string, List<bool>>();
            foreach (var c in conds)
                if (choices[rnd.Next(choices.Length)] is { } v) plan[c] = v;
            var parameters = TestModel.Params(
                ("PATIENT_BODY_TYPE", new[] { "THIN", "NORMAL", "OBESE" }[rnd.Next(3)]),
                ("SKIN_CLOSURE_TYPE", new[] { "STANDARD", "AESTHETIC" }[rnd.Next(2)]),
                ("MESO_VESSELS", Enumerable.Range(0, rnd.Next(0, 5)).Select(_ => new { caliber = new[] { "SMALL", "MEDIUM", "LARGE" }[rnd.Next(3)] }).ToArray()));

            var engine = new Arqenion.Api.Services.FlowEngine(model);
            var reference = engine.Start(new(plan), new(parameters));
            for (var node = reference.Path[0]; node is not null; node = engine.Advance(reference, node).Next) { }

            var service = TestModel.Service();
            var session = new SessionRecord { Id = $"soak-{seed}", Score = 100, RawScore = 100 };
            service.Start(session, new Scenario
            {
                ScenarioId = $"soak-{seed}",
                Patient = new PatientProfile { BodyType = parameters["PATIENT_BODY_TYPE"].GetString()! },
                Conditions = plan.ToDictionary(c => c.Key, c => JsonSerializer.SerializeToElement(c.Value)),
                Parameters = parameters.Where(p => p.Key != "PATIENT_BODY_TYPE").ToDictionary(p => p.Key, p => p.Value),
                SurgeonRequestMode = rnd.NextDouble() < 0.8 ? "random" : "off"
            });
            for (var guard = 0; session.CurrentNodeId is not null; guard++)
            {
                Assert.True(guard < 3000, $"seed {seed}: akış bitmedi");
                var state = FlowProcedureService.Load(session);
                var x = model.Expect(session.CurrentNodeId, state.Parameters);
                if (rnd.NextDouble() < 0.4)
                {
                    var ev = RandomEvent(rnd, codes, x);
                    var verdict = Oracle(x, state, ev);
                    var o = service.Validate(session, ev);
                    AssertInvariants(session, o);
                    if (verdict is { } accept)
                        Assert.True(accept == (o.Deviation is null), $"seed {seed} düğüm {state.Path[^1]}: {ev.EventType} {ev.InstrumentCode}/{ev.SutureCode} → {o.Message}");
                    continue;
                }
                foreach (var ev in TestModel.CorrectEvents(session).ToList())
                {
                    var o = service.Validate(session, ev);
                    AssertInvariants(session, o);
                    Assert.True(o.Success, $"seed {seed}: doğru olay reddedildi: {o.Message}");
                }
            }
            Assert.Equal(reference.Path, FlowProcedureService.Load(session).Path);
            Assert.Equal(session.ComplicationsTriggered, session.ComplicationsResolved);
        }
    }

    private static void AssertInvariants(SessionRecord session, ValidationOutcome o)
    {
        var b = TestModel.Model.Scoring.Bounds;
        Assert.InRange(session.Score, b.Min, b.Max);
        Assert.InRange(session.ComplicationsResolved, 0, session.ComplicationsTriggered);
        Assert.True(o.Success || o.Deviation is not null || o.Message.StartsWith("İpucu"), o.Message);
        if (session.CurrentNodeId is not null)
        {
            Assert.NotNull(o.NextStep);
            Assert.Equal(session.CurrentNodeId, o.NextNodeId);
            Assert.Equal(FlowModelStore.MainStep(session.CurrentNodeId), o.NextStepId);
        }
        else Assert.True(o.Completed);
    }

    private static EventRequest RandomEvent(Random rnd, string[] codes, NodeExpectation x)
    {
        var type = EventTypes[rnd.Next(EventTypes.Length)];
        string? Pick(IReadOnlyList<string> list) => list.Count > 0 && rnd.NextDouble() < 0.5 ? list[rnd.Next(list.Count)] : codes[rnd.Next(codes.Length)];
        var ev = new EventRequest
        {
            EventType = type,
            InstrumentCode = rnd.NextDouble() < 0.15 ? null : Pick(x.Deliverable.Concat(x.Companions).ToList()),
            SutureCode = rnd.NextDouble() < 0.5 ? null : Pick(x.Sutures),
            Target = rnd.NextDouble() < 0.8 ? "SURGEON" : rnd.NextDouble() < 0.5 ? null : "DIRTY_AREA"
        };
        if (type == "count_confirmed" && rnd.NextDouble() < 0.9)
        {
            ev.Counts = new();
            foreach (var c in new[] { "COMPRESS", "INSTRUMENT", "NEEDLE" })
                if (rnd.NextDouble() < 0.7) ev.Counts[c] = rnd.Next(0, 45);
        }
        return ev;
    }

    // Bağımsız karar kuralı (motor kodundan değil, modelin listelerinden): olay kabul edilmeli mi?
    // null = bu olay türü için hüküm yok (ipucu: ne kabul ne sapma).
    private static bool? Oracle(NodeExpectation x, FlowState state, EventRequest ev)
    {
        if (ev.EventType == "hint_requested") return null;
        if (ev.EventType is "sterile_area_violated" or "instrument_dropped") return false;
        var delivering = ev.EventType is "instrument_delivered" or "instrument_placed";
        switch (x.Kind)
        {
            case "confirm": return ev.EventType == "step_confirmed";
            case "count":
                return ev.EventType == "count_confirmed" && ev.Counts is not null
                       && ev.Counts.TryGetValue(x.CountCategory!, out var n) && n == state.Counts[x.CountCategory!];
        }
        if (!delivering) return false;
        if (!string.IsNullOrEmpty(ev.Target) && ev.Target != "SURGEON") return false;

        var model = TestModel.Model;
        string? instrument = ev.InstrumentCode, suture = ev.SutureCode;
        if (suture is null && model.IsSuture(instrument)) (suture, instrument) = (instrument, null);

        // Model listesi: graph.json kabul listesi (parametre kuralı uygulanmış) + adımın destek aletleri.
        var allowed = x.Accepted.Where(c => !model.IsSuture(c) && c != "HAND").ToHashSet();
        if (x.ByParameter is { } bp)
        {
            var rule = model.Node(state.Path[^1])!.ParameterRules.First(r => r.Parameter == bp.Parameter);
            allowed.ExceptWith(rule.ExpectedInstruments.Values.SelectMany(v => v).Except(bp.Instruments));
        }
        if (instrument is not null && !allowed.Contains(instrument) && !x.Companions.Contains(instrument)) return false;
        if (instrument is null && allowed.Count > 0) return false;
        if (x.Sutures.Count == 0) return suture is null && ClampOk(x, state, instrument) && RequestOk(x, state, instrument, suture);
        return suture is not null && x.Sutures.Contains(suture) && ClampOk(x, state, instrument) && RequestOk(x, state, instrument, suture);
    }

    // Cerrah istemi: bir seçenek grubundan istenen dışında bir alternatif uzatılamaz.
    private static bool RequestOk(NodeExpectation x, FlowState state, string? instrument, string? suture)
    {
        var node = state.Path[^1];
        var requested = state.Requests.GetValueOrDefault(node) ?? new();
        foreach (var g in TestModel.Model.ChoiceGroups(node, x))
        {
            var codes = g.SelectMany(o => o).ToHashSet();
            if (!codes.Overlaps(requested)) continue;
            if (instrument is not null && codes.Contains(instrument) && !requested.Contains(instrument)) return false;
            if (suture is not null && codes.Contains(suture) && !requested.Contains(suture)) return false;
        }
        return true;
    }

    // Klemp adedi damara bağlı adımda beklenmeyen tipte hemostatik klemp kabul edilmez.
    private static bool ClampOk(NodeExpectation x, FlowState state, string? instrument)
        => x.Clamps.Count == 0 || instrument is null
           || !TestModel.Model.Node(state.Path[^1])!.DynamicQuantity!.InstrumentByCaliber.ContainsValue(instrument)
           || x.Clamps.Any(c => c.Code == instrument);
}
