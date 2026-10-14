using System.Text.Json;
using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// 60 adımlık modelin (v2.4) olay doğrulaması. Doğru/yanlış, puan ve sıradaki düğüm burada belirlenir; AI'a karar bırakılmaz.
public sealed class FlowProcedureService
{
    public const string ModelKey = "v2.4";
    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, int> DefaultCounts = new() { ["COMPRESS"] = 10, ["INSTRUMENT"] = 20, ["NEEDLE"] = 3 };
    private static readonly Dictionary<string, string> CountNames = new() { ["COMPRESS"] = "kompres", ["INSTRUMENT"] = "alet", ["NEEDLE"] = "iğne" };

    private readonly FlowModelStore _model;
    private readonly FlowEngine _engine;
    private readonly ScoringService _scoring;
    private readonly FlowClinicalText _text;

    public FlowProcedureService(FlowModelStore model, FlowEngine engine, ScoringService scoring, FlowClinicalText text)
    {
        _model = model;
        _engine = engine;
        _scoring = scoring;
        _text = text;
    }

    // ----- Seans başlangıcı: senaryodan koşul planı, hasta parametreleri ve masadaki sayılar -----

    public FlowState Start(SessionRecord session, Scenario scenario, string? seed = null)
    {
        var conditions = new Dictionary<string, List<bool>>();
        if (scenario.Patient.HasAdhesion) conditions["ADHESIONS_PRESENT"] = new() { true };
        foreach (var (k, v) in scenario.Conditions)
            conditions[k] = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetBoolean()).ToList() : new() { v.GetBoolean() };

        var parameters = new Dictionary<string, JsonElement>(scenario.Parameters)
        {
            ["PATIENT_BODY_TYPE"] = JsonSerializer.SerializeToElement(scenario.Patient.BodyType)
        };
        var state = _engine.Start(conditions, parameters, scenario.IncludePreop);
        state.RecordedCounts = new(scenario.Counts ?? DefaultCounts);
        state.Counts = new(state.RecordedCounts);
        if (conditions.TryGetValue("COUNT_MISMATCH", out var mismatch) && mismatch.FirstOrDefault()
            && state.Counts.TryGetValue(scenario.MissingItem, out var n) && n > 0)
            state.Counts[scenario.MissingItem] = n - 1;   // senaryo bir malzemeyi eksik bıraktı

        state.Requests = SurgeonRequests(seed ?? session.Id, scenario, state);
        state.AdaptiveMode = scenario.AdaptiveMode;
        state.FixedConditions = conditions.Keys.ToList();

        session.ProcedureModel = ModelKey;
        session.CurrentNodeId = state.Path[0];
        session.CurrentStepId = FlowModelStore.MainStep(state.Path[0]);
        Save(session, state);
        return state;
    }

    // Hekim tercihine bırakılan adımlarda cerrahın istemi seans başında belirlenir ve kayda geçer (tekrar oynatılabilir).
    private Dictionary<string, List<string>> SurgeonRequests(string seed, Scenario scenario, FlowState state)
    {
        var requests = new Dictionary<string, List<string>>();
        if (scenario.SurgeonRequestMode == "off") return requests;
        var rnd = new Random(StableHash(seed));
        foreach (var n in _model.PlayableNodes)
        {
            var groups = _model.ChoiceGroups(n.Id, _model.Expect(n.Id, state.Parameters));
            if (groups.Count == 0) continue;
            // Her karardan bir seçenek; senaryo bir alet sabitlediyse onu içeren seçenek. İstenen = seçeneğin eşdeğer aletleri.
            requests[n.Id] = groups.SelectMany(g =>
                scenario.SurgeonRequests.TryGetValue(n.Id, out var fixedCode) && g.FirstOrDefault(o => o.Contains(fixedCode)) is { } chosen
                    ? chosen : g[rnd.Next(g.Count)]).ToList();
        }
        return requests;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var ch in text) h = (h ^ ch) * 16777619;
            return h;
        }
    }

    // Cerrah istemi uygulanmış kabul listeleri: gruptaki diğer alternatifler bu seansta yanlış sayılır.
    public (List<string> Deliverable, List<string> Sutures, List<string> Requested) Allowed(string nodeId, NodeExpectation x, FlowState state)
    {
        var requested = state.Requests.GetValueOrDefault(nodeId) ?? new();
        if (requested.Count == 0) return (x.Deliverable, x.Sutures, requested);
        var excluded = _model.ChoiceGroups(nodeId, x).SelectMany(g => g.SelectMany(o => o)).Except(requested).ToHashSet();
        return (x.Deliverable.Where(c => !excluded.Contains(c)).ToList(), x.Sutures.Where(c => !excluded.Contains(c)).ToList(), requested);
    }

    // ----- VR olay sözleşmesi (docs/vr-events.md) -----
    // Sözleşmeye uymayan olay (bilinmeyen tür, katalogda olmayan kod, sayısız sayım) entegrasyon hatasıdır:
    // kursiyerin hatası sayılmaz, puanı etkilemez; çağıran 400 alır.
    public static readonly string[] EventTypes =
        { "instrument_delivered", "instrument_placed", "step_confirmed", "count_confirmed", "checklist_confirmed",
          "hint_requested", "sterile_area_violated", "instrument_dropped" };
    public static readonly string[] Targets = { "SURGEON", "DIRTY_AREA", "PATHOLOGY_CONTAINER" };
    public static readonly string[] CountCategories = { "COMPRESS", "INSTRUMENT", "NEEDLE" };

    public (string Field, string Message, IEnumerable<string>? Allowed)? ContractError(EventRequest ev)
    {
        if (!EventTypes.Contains(ev.EventType))
            return ("eventType", $"Bilinmeyen olay türü: '{ev.EventType}'.", EventTypes);
        if (!string.IsNullOrEmpty(ev.Target) && !Targets.Contains(ev.Target))
            return ("target", $"Bilinmeyen hedef: '{ev.Target}'.", Targets);
        foreach (var (field, code) in new[] { ("instrumentCode", ev.InstrumentCode), ("sutureCode", ev.SutureCode) })
            if (code is not null && !_model.Catalog.ContainsKey(code))
                return (field, $"Katalogda olmayan kod: '{code}'. Katalog: GET /api/procedure (instrumentCatalog, sutureCatalog).", null);
        if (ev.EventType is "instrument_delivered" or "instrument_placed" && ev.InstrumentCode is null && ev.SutureCode is null)
            return ("instrumentCode", "Teslim olayında instrumentCode ya da sutureCode gerekir.", null);
        if (ev.EventType == "checklist_confirmed")
        {
            if (ev.CheckedItems is null)
                return ("checkedItems", "Kontrol listesi olayında checkedItems gerekir (işaretlenen madde kimlikleri, ör. [\"PRE01.1\"]).", null);
            if (ev.CheckedItems.FirstOrDefault(i => !_model.IsChecklistItem(i)) is { } unknown)
                return ("checkedItems", $"Bilinmeyen kontrol listesi maddesi: '{unknown}'. Maddeler: adım görünümündeki checklist.", null);
        }
        if (ev.EventType == "count_confirmed")
        {
            if (ev.Counts is null || ev.Counts.Count == 0)
                return ("counts", "Sayım olayında counts gerekir (ör. {\"COMPRESS\": 10}).", CountCategories);
            if (ev.Counts.Keys.FirstOrDefault(k => !CountCategories.Contains(k)) is { } bad)
                return ("counts", $"Bilinmeyen sayım kategorisi: '{bad}'.", CountCategories);
            if (ev.Counts.Values.Any(v => v < 0))
                return ("counts", "Sayım negatif olamaz.", null);
        }
        return null;
    }

    public static FlowState Load(SessionRecord session)
        => string.IsNullOrEmpty(session.FlowStateJson)
            ? new FlowState()
            : JsonSerializer.Deserialize<FlowState>(session.FlowStateJson, StateJson) ?? new FlowState();

    private static void Save(SessionRecord session, FlowState state)
        => session.FlowStateJson = JsonSerializer.Serialize(state, StateJson);

    // ----- Olay doğrulama -----

    public ValidationOutcome Validate(SessionRecord session, EventRequest ev)
    {
        var state = Load(session);
        var node = _model.Node(session.CurrentNodeId);
        if (node is null)
            return new ValidationOutcome { Success = false, CurrentStepId = session.CurrentStepId ?? 0, Completed = true,
                                           Message = "Seans tamamlanmış veya geçerli bir adım bulunamadı." };

        var x = _model.Expect(node.Id, state.Parameters);
        var outcome = ValidateOn(session, state, node, x, ev);
        outcome.CurrentNodeId ??= node.Id;
        outcome.CurrentStepId = FlowModelStore.MainStep(node.Id);
        Save(session, state);
        return outcome;
    }

    private ValidationOutcome ValidateOn(SessionRecord session, FlowState state, FlowNode node, NodeExpectation x, EventRequest ev)
    {
        if (ev.EventType == "hint_requested")
        {
            session.HintsUsed++;
            _scoring.Apply(session, -_model.Scoring.HintPenalty, _model.Scoring.Bounds);
            return Stay(node, state, $"İpucu: {node.Label}. {Instruction(node, x, state)}", -_model.Scoring.HintPenalty);
        }
        if (ev.EventType == "sterile_area_violated")
        {
            session.SterileViolations++;
            return Deviate(session, state, node, x, "STERILE_VIOLATION", null, null, "Steril alan ihlali algılandı.");
        }
        if (ev.EventType == "instrument_dropped")
            return Deviate(session, state, node, x, "INSTRUMENT_DROPPED", null, ev.InstrumentCode, "Alet düşürüldü; sterilite kaybı.");

        var delivering = ev.EventType is "instrument_delivered" or "instrument_placed";
        switch (x.Kind)
        {
            case "confirm":
                if (delivering)
                    return Deviate(session, state, node, x, "WRONG_INSTRUMENT", null, ev.SutureCode ?? ev.InstrumentCode,
                        "Bu adımda yeni alet verilmez; cerrahı izleyip adımı onaylayın.");
                if (ev.EventType != "step_confirmed")
                    return Deviate(session, state, node, x, "WRONG_ORDER", x.ExpectedEvent, ev.EventType, "Bu adımda beklenen işlem: adımı onaylamak.");
                return Complete(session, state, node, x);

            case "checklist":
                if (ev.EventType != "checklist_confirmed")
                    return Deviate(session, state, node, x, "WRONG_ORDER", x.ExpectedEvent, ev.EventType,
                        "Ameliyat öncesi bu bölümün kontrol listesi onaylanmalıdır.");
                var checkedItems = (ev.CheckedItems ?? new()).ToHashSet();
                var missing = x.Checklist.Where(i => !i.Optional && !checkedItems.Contains(i.Id)).ToList();
                if (missing.Count > 0)
                {
                    var required = x.Checklist.Count(i => !i.Optional);
                    return Deviate(session, state, node, x, "CHECKLIST_INCOMPLETE", string.Join(" / ", missing.Select(i => i.Id)),
                        $"{required - missing.Count}/{required}",
                        $"Kontrol listesinde eksik: {string.Join("; ", missing.Select(i => i.Name))}.");
                }
                return Complete(session, state, node, x);

            case "count":
                if (ev.EventType != "count_confirmed")
                    return Deviate(session, state, node, x, "WRONG_ORDER", x.ExpectedEvent, ev.EventType, "Bu adımda sayım yapılmalıdır.");
                var category = x.CountCategory!;
                var truth = state.Counts.GetValueOrDefault(category);
                int? given = ev.Counts is not null && ev.Counts.TryGetValue(category, out var g) ? g : null;
                if (given != truth)
                    return Deviate(session, state, node, x, "COUNT_MISMATCH", $"{category}:{truth}", $"{category}:{given?.ToString() ?? "-"}",
                        $"Sayım masadaki {CountNames[category]} sayısıyla uyuşmuyor; tek tek ve sesli olarak yeniden sayın.");
                return Complete(session, state, node, x);
        }

        // Alet teslimi.
        if (!delivering)
            return Deviate(session, state, node, x, "WRONG_ORDER", x.ExpectedEvent, ev.EventType,
                "Bu adımda cerraha alet teslim edilmelidir.");
        if (!string.IsNullOrEmpty(ev.Target) && ev.Target != "SURGEON")
            return Deviate(session, state, node, x, "WRONG_ORDER", "SURGEON", ev.Target, "Alet yanlış alana yönlendirildi.");

        var instrument = ev.InstrumentCode;
        var suture = ev.SutureCode;
        if (suture is null && _model.IsSuture(instrument))
        {
            suture = instrument;   // iplik tek başına uzatıldı (ör. 26. adım hazırlık)
            instrument = null;
        }
        var expected = string.Join(" / ", x.Deliverable);
        var expectedSuture = string.Join(" / ", x.Sutures);

        if (instrument is not null && !x.Deliverable.Contains(instrument) && !x.Companions.Contains(instrument))
            return Deviate(session, state, node, x, "WRONG_INSTRUMENT", expected.Length > 0 ? expected : expectedSuture, instrument,
                "Bu aşamada uygun olmayan bir alet uzatıldı.");
        if (instrument is null && x.Deliverable.Count > 0)
            return Deviate(session, state, node, x, "WRONG_INSTRUMENT", expected, null, "Cerraha uzatılacak alet seçilmedi.");
        if (x.Sutures.Count == 0 && suture is not null)
            return Deviate(session, state, node, x, "WRONG_INSTRUMENT", expected, suture, "Bu adımda iplik verilmez.");
        if (x.Sutures.Count > 0 && suture is null)
            return Deviate(session, state, node, x, "WRONG_INSTRUMENT", expectedSuture, null, "Bu adım için iplik seçilmedi.");
        if (suture is not null && !x.Sutures.Contains(suture))
            return Deviate(session, state, node, x, "WRONG_INSTRUMENT", expectedSuture, suture, "Bu aşamada yanlış iplik seçildi.");

        // Kabul edilen bir alternatif, ama cerrah başkasını istedi (ör. S51-E1, S56-E2: "istenen X yerine Y").
        var (_, _, requested) = Allowed(node.Id, x, state);
        foreach (var group in _model.ChoiceGroups(node.Id, x))
        {
            var codes = group.SelectMany(o => o).ToHashSet();
            var wanted = codes.Where(requested.Contains).ToList();
            if (wanted.Count == 0) continue;
            var given = codes.Contains(instrument ?? "") ? instrument : codes.Contains(suture ?? "") ? suture : null;
            if (given is not null && !wanted.Contains(given))
                return Deviate(session, state, node, x, "WRONG_INSTRUMENT", string.Join(" / ", wanted), given,
                    $"Cerrah {string.Join(" / ", wanted.Select(_model.InstrumentName))} istedi; istem doğrulanmadan farklı bir alternatif uzatıldı.");
        }

        // Damar sayısına bağlı klemp adedi: her klemp ayrı teslim edilir; hepsi verilince adım tamamlanır.
        if (x.Clamps.Count > 0)
        {
            var need = x.Clamps.FirstOrDefault(c => c.Code == instrument && state.Delivered.GetValueOrDefault(c.Code) < c.Qty);
            if (need is not null) state.Delivered[need.Code] = state.Delivered.GetValueOrDefault(need.Code) + 1;
            var remaining = Remaining(x, state);
            if (remaining.Count > 0)
            {
                session.CorrectActions++;
                session.ConsecutiveErrors = 0;
                var left = string.Join(", ", remaining.Select(c => $"{c.Qty}× {_model.InstrumentName(c.Code)}"));
                return Stay(node, state, need is not null
                    ? $"Doğru klemp. Kalan: {left}."
                    : $"Kabul edildi. Damarlar için klemp bekleniyor: {left}.", 0, success: true);
            }
        }
        return Complete(session, state, node, x);
    }

    private ValidationOutcome Complete(SessionRecord session, FlowState state, FlowNode node, NodeExpectation x)
    {
        var s = _model.Scoring;
        var gain = s.StepOverrides.GetValueOrDefault(node.Id)?.Success ?? s.Success.GetValueOrDefault(x.Kind);
        if (node.Kind != "step") gain += s.BranchStepBonus;

        session.CorrectActions++;
        session.ConsecutiveErrors = 0;

        var (next, condition) = _engine.Advance(state, node.Id);
        FlowBranchDto? branch = null;
        var message = next is null ? "Prosedür tamamlandı." : "Doğru işlem.";
        if (condition is not null && state.OpenBranch is null)
        {
            // Karar noktasında dala girildi: komplikasyon/ek durum başladı.
            state.OpenBranch = new OpenBranch { Condition = condition, Origin = node.Id, Resume = node.Next };
            session.ComplicationsTriggered++;
            branch = BranchDto(condition, node.Id, next, resolved: false);
            message = $"Doğru işlem. DURUM — {branch.Title}.";
        }
        else if (state.OpenBranch is { } open && next == open.Resume)
        {
            // Daldan ana akışa dönüldü: durum yönetildi.
            state.OpenBranch = null;
            session.ComplicationsResolved++;
            gain += s.BranchResolvedBonus;
            branch = BranchDto(open.Condition, node.Id, next, resolved: true);
            message = $"Doğru işlem. {branch.Title} — yönetildi, ana akışa dönülüyor.";
        }

        _scoring.Apply(session, gain, s.Bounds);
        session.CurrentNodeId = next;
        session.CurrentStepId = next is null ? null : FlowModelStore.MainStep(next);

        return new ValidationOutcome
        {
            Success = true,
            CurrentNodeId = node.Id,
            NextNodeId = next,
            NextStepId = next is null ? null : FlowModelStore.MainStep(next),
            Message = message,
            AllowRetry = false,
            Completed = next is null,
            ScoreChange = gain,
            NextStep = View(next, state),
            Branch = branch
        };
    }

    private ValidationOutcome Deviate(SessionRecord session, FlowState state, FlowNode node, NodeExpectation x, string type,
        string? expected, string? actual, string message)
    {
        var s = _model.Scoring;
        session.WrongActions++;
        session.ConsecutiveErrors++;
        var basePenalty = s.StepOverrides.GetValueOrDefault(node.Id)?.Penalties.GetValueOrDefault(type)
                          ?? s.Penalties.GetValueOrDefault(type, 5);
        var penalty = basePenalty + (session.ConsecutiveErrors - 1) * s.EscalationPerConsecutiveError;
        _scoring.Apply(session, -penalty, s.Bounds);

        var allowed = Allowed(node.Id, x, state);
        IReadOnlyCollection<string> wanted = type == "CHECKLIST_INCOMPLETE" ? (expected ?? "").Split(" / ")
            : x.Sutures.Count > 0 && _model.IsSuture(actual) ? allowed.Sutures : allowed.Deliverable;
        var severity = Escalate(_text.Entry(node.Id, type, actual, wanted).Severity, session.ConsecutiveErrors);
        var adaptive = ApplyAdaptiveRules(state, node, type, actual, wanted);
        return new ValidationOutcome
        {
            Success = false,
            CurrentNodeId = node.Id,
            NextNodeId = node.Id,
            NextStepId = FlowModelStore.MainStep(node.Id),
            Deviation = new DeviationDto { DeviationType = type, Expected = expected, Actual = actual, Severity = severity },
            Message = message,
            AllowRetry = true,
            ScoreChange = -penalty,
            NextStep = View(node.Id, state),
            Adaptive = adaptive.Count > 0 ? adaptive : null
        };
    }

    // Hataya göre dal (Procedures/adaptive-rules.json): hata, eşleştiği onaylı senaryonun komplikasyonu kuralla örtüşüyorsa
    // koşulu doğru yapar. Dal hemen açılmaz; motor onu koşulun karar adımında açar (ör. kanama → Adım 40).
    private List<AdaptiveTriggerDto> ApplyAdaptiveRules(FlowState state, FlowNode node, string type, string? actual, IReadOnlyCollection<string> wanted)
    {
        var fired = new List<AdaptiveTriggerDto>();
        if (state.AdaptiveMode == "off") return fired;
        var step = FlowModelStore.MainStep(node.Id);
        ErrorScenario? scenario = null;
        var matched = false;
        foreach (var rule in _model.AdaptiveRules)
        {
            if (!rule.DeviationTypes.Contains(type) || step < rule.FromStep || step > rule.ToStep) continue;
            if (state.FixedConditions.Contains(rule.Condition) || state.AdaptiveDecisions.Any(d => d.Condition == rule.Condition)) continue;
            if (!_model.Graph.Conditions.TryGetValue(rule.Condition, out var condition)) continue;
            var outcome = !_model.BranchConditions.Contains(rule.Condition);
            // Koşul karar adımında zaten değerlendirildiyse geriye dönük değişmez.
            if (!outcome && state.Evaluations.Any(e => e.Key.EndsWith("|" + rule.Condition))) continue;
            if (rule.Complication is not null)
            {
                if (!matched) { scenario = _text.Match(node.Id, actual, wanted); matched = true; }
                if (scenario is null || !System.Text.RegularExpressions.Regex.IsMatch(
                        string.Join(" ", scenario.PossibleComplications).ToLower(new System.Globalization.CultureInfo("tr-TR")), rule.Complication))
                    continue;
            }

            if (!outcome) state.Conditions[rule.Condition] = new() { true };
            state.AdaptiveDecisions.Add(new AdaptiveDecision
            {
                Condition = rule.Condition, AtNode = node.Id, DeviationType = type, ScenarioId = scenario?.Id,
                Outcome = outcome, Message = rule.Message
            });
            fired.Add(new AdaptiveTriggerDto
            {
                Condition = rule.Condition, Title = condition.Description, Message = rule.Message,
                EvaluatedAt = outcome ? null : condition.EvaluatedAt, Outcome = outcome, ScenarioId = scenario?.Id
            });
        }
        return fired;
    }

    private ValidationOutcome Stay(FlowNode node, FlowState state, string message, int scoreChange, bool success = false)
        => new()
        {
            Success = success,
            CurrentNodeId = node.Id,
            NextNodeId = node.Id,
            NextStepId = FlowModelStore.MainStep(node.Id),
            Message = message,
            AllowRetry = true,
            ScoreChange = scoreChange,
            NextStep = View(node.Id, state)
        };

    private static string Escalate(string baseSeverity, int streak)
    {
        var rank = baseSeverity switch { "LOW" => 1, "MEDIUM" => 2, "HIGH" => 3, "CRITICAL" => 4, _ => 2 } + Math.Max(0, streak - 1);
        return rank switch { <= 1 => "LOW", 2 => "MEDIUM", 3 => "HIGH", _ => "CRITICAL" };
    }

    private FlowBranchDto BranchDto(string condition, string from, string? to, bool resolved)
    {
        var c = _model.Graph.Conditions.GetValueOrDefault(condition);
        return new FlowBranchDto
        {
            Condition = condition,
            Title = c?.Description ?? condition,
            Source = c?.Source ?? "",
            FromNodeId = from,
            ToNodeId = to,
            Resolved = resolved
        };
    }

    private static List<ClampNeed> Remaining(NodeExpectation x, FlowState state)
        => x.Clamps.Select(c => new ClampNeed { Code = c.Code, Qty = c.Qty - state.Delivered.GetValueOrDefault(c.Code) })
            .Where(c => c.Qty > 0).ToList();

    // ----- Görünüm -----

    public StepView? View(string? nodeId, FlowState state)
    {
        var node = _model.Node(nodeId);
        if (node is null) return null;
        var x = _model.Expect(node.Id, state.Parameters);
        var condition = node.Condition ?? state.OpenBranch?.Condition;
        var practice = _model.Master(node.Id)?.Scrub.CorrectPractice ?? "";
        var dot = practice.IndexOf(". ", StringComparison.Ordinal);
        var clamps = x.Clamps.Count > 0 ? Remaining(x, state) : null;
        var (deliverable, sutures, requested) = Allowed(node.Id, x, state);
        return new StepView
        {
            StepId = FlowModelStore.MainStep(node.Id),
            NodeId = node.Id,
            PhaseNo = node.PhaseId is not null ? _model.PhaseNo.GetValueOrDefault(node.PhaseId) : 0,
            Phase = node.Kind == "preop" ? "Ameliyat öncesi" : _model.PhaseName(node),
            Title = node.Label,
            ExpectedEvent = x.ExpectedEvent,
            Kind = x.Kind,
            AllowedInstruments = clamps is { Count: > 0 } ? clamps.Select(c => c.Code).ToList() : deliverable,
            RequiredSuture = sutures.FirstOrDefault(),
            RequiredSutures = sutures,
            SurgeonRequest = requested.Count > 0 ? string.Join(" / ", requested.Select(_model.InstrumentName)) : null,
            Clamps = clamps,
            Target = x.Kind == "delivery" ? "SURGEON" : null,
            CountCategory = x.CountCategory,
            TableCount = x.CountCategory is { } cc ? state.Counts.GetValueOrDefault(cc) : null,
            RecordedCount = x.CountCategory is { } rc ? state.RecordedCounts.GetValueOrDefault(rc) : null,
            Condition = condition,
            ConditionText = condition is not null ? _model.Graph.Conditions.GetValueOrDefault(condition)?.Description : null,
            IsBranch = node.Kind != "step" || state.OpenBranch is not null,
            Instruction = Instruction(node, x, state),
            ClinicalNote = node.Kind == "preop" ? "Ameliyat öncesi hazırlık" : dot < 0 ? practice : practice[..(dot + 1)],
            Checklist = x.Kind == "checklist" ? x.Checklist : null
        };
    }

    public string Instruction(FlowNode node, NodeExpectation x, FlowState state)
    {
        switch (x.Kind)
        {
            case "checklist":
                var required = x.Checklist.Count(i => !i.Optional);
                return $"Ameliyat öncesi kontrol listesi: {required} maddeyi kontrol edip onaylayın" +
                       (x.Checklist.Count > required ? " (koşullu maddeler gerekiyorsa)." : ".");
            case "count":
                var c = x.CountCategory!;
                return $"Sayım: {CountNames[c]} — masadakileri sirküleyle birlikte sayıp sonucu girin (başlangıç kaydı {state.RecordedCounts.GetValueOrDefault(c)}).";
            case "confirm":
                return "Yeni alet verilmez — cerrahı izleyin, steril alanı koruyun ve adımı onaylayın.";
        }
        var clamps = x.Clamps.Count > 0 ? Remaining(x, state) : new();
        var (deliverable, sutures, requested) = Allowed(node.Id, x, state);
        var items = clamps.Count > 0
            ? string.Join(", ", clamps.Select(k => $"{k.Qty}× {_model.InstrumentName(k.Code)}"))
            : string.Join(" / ", deliverable.Select(_model.InstrumentName));
        var suture = sutures.Count > 0 ? $" + iplik: {string.Join(" / ", sutures.Select(_model.InstrumentName))}" : "";
        var request = requested.Count > 0 ? $"Cerrah istiyor: {string.Join(" / ", requested.Select(_model.InstrumentName))}. " : "";
        return items.Length > 0 ? $"{request}Cerraha teslim edin: {items}{suture}" : $"{request}Cerraha teslim edin{suture}";
    }
}
