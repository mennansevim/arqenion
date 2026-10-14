using System.Text.Json;
using System.Text.RegularExpressions;
using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// Bir düğümde kursiyerden beklenen: alet teslimi, onay ya da sayım; hasta parametreleri uygulanmış.
public sealed class NodeExpectation
{
    // Python flow_engine.expected_instruments ile aynı alanlar (golden testler bunları karşılaştırır).
    public List<string> Accepted { get; init; } = new();
    public (string Parameter, string Value, List<string> Instruments)? ByParameter { get; init; }
    public List<ClampNeed> Clamps { get; init; } = new();

    // Motorun doğrulaması için türetilenler.
    public string Kind { get; init; } = "confirm";          // delivery | confirm | count
    public List<string> Deliverable { get; init; } = new(); // cerraha uzatılacak aletler (iplik ve el hariç)
    public List<string> Sutures { get; init; } = new();     // kabul edilen iplikler; boşsa iplik verilmez
    public List<string> Companions { get; init; } = new();  // adımın destek aletleri (ör. ipliği taşıyan portegü); yanlış sayılmaz
    public string? CountCategory { get; init; }             // COMPRESS | INSTRUMENT | NEEDLE
    public List<ChecklistItem> Checklist { get; init; } = new();   // ameliyat öncesi kontrol listesi

    public string ExpectedEvent => Kind switch
    {
        "delivery" => "instrument_delivered",
        "count" => "count_confirmed",
        "checklist" => "checklist_confirmed",
        _ => "step_confirmed"
    };
}

// 60 adımlık akış modelini (graph.json + ana model) ve puanlama ayarını yükler. Karar mantığı FlowEngine'dedir.
public sealed class FlowModelStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> DeliveredUsages = new() { "PRIMARY", "SUPPORT", "ALTERNATIVE" };
    private const string SurgeonChoiceLigature = "LIGATURE_SURGEON_CHOICE";

    private readonly Dictionary<string, FlowNode> _nodes;
    private readonly Dictionary<string, List<StepInstrument>> _instrumentUsage = new();
    private readonly Dictionary<int, MasterStep> _masterSteps;

    public FlowModelStore(string modelDir, string version, string scoringPath, string? descriptorsPath = null,
        string? adaptiveRulesPath = null)
    {
        var baseName = Path.Combine(modelDir, $"open-appendectomy-v{version}");
        Graph = Load<FlowGraphFile>($"{baseName}.graph.json");
        var master = Load<MasterModelFile>($"{baseName}.json");
        Scoring = Load<ScoringConfig>(scoringPath);

        Version = Graph.Version;
        Catalog = master.Catalogs.Instruments;
        if (descriptorsPath is not null && File.Exists(descriptorsPath))
            Descriptors = Load<DescriptorsFile>(descriptorsPath).Descriptors;
        if (adaptiveRulesPath is not null && File.Exists(adaptiveRulesPath))
            AdaptiveRules = Load<AdaptiveRulesFile>(adaptiveRulesPath).Rules;
        BranchConditions = Graph.Nodes.SelectMany(n => n.Branches).Select(b => b.When).ToHashSet();
        _nodes = Graph.Nodes.ToDictionary(n => n.Id);
        _masterSteps = master.Steps.ToDictionary(s => s.Step);
        foreach (var s in master.Steps) _instrumentUsage[s.Step.ToString()] = s.Instruments;
        foreach (var s in master.Subflows.SelectMany(f => f.Steps)) _instrumentUsage[s.Id] = s.Instruments;

        Checklists = master.Preoperative.ToDictionary(p => p.GetProperty("id").GetString()!, BuildChecklist);
        PlayableNodes = Graph.Nodes
            .OrderBy(n => MainStep(n.Id)).ThenBy(n => n.Id.Length).ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
        MainStepCount = PlayableNodes.Count(n => n.Kind is "step" or "conditional");
        PhaseNo = Graph.Phases.Select((p, i) => (p.Id, i + 1)).ToDictionary(x => x.Id, x => x.Item2);
    }

    public FlowGraphFile Graph { get; }
    public string Version { get; }
    public ScoringConfig Scoring { get; }
    public Dictionary<string, CatalogInstrument> Catalog { get; }
    public Dictionary<string, List<string>> Descriptors { get; private set; } = new();
    public List<AdaptiveRule> AdaptiveRules { get; private set; } = new();
    // Bir dalın koşulu olanlar; diğerleri (ör. SURGICAL_SITE_INFECTION) yalnız sonuçtur, akışı değiştirmez.
    public HashSet<string> BranchConditions { get; private set; } = new();
    public IReadOnlyList<FlowNode> PlayableNodes { get; }
    public int MainStepCount { get; }
    public Dictionary<string, int> PhaseNo { get; }
    public string FirstNodeId => "1";
    public string PreopEntryNodeId => Graph.Entry ?? "PRE01";   // senaryo IncludePreop ise seans buradan başlar
    public IReadOnlyDictionary<string, List<ChecklistItem>> Checklists { get; }
    public bool IsChecklistItem(string id) => Checklists.Values.Any(l => l.Any(i => i.Id == id));

    public FlowNode? Node(string? id) => id is not null && _nodes.TryGetValue(id, out var n) ? n : null;

    public MasterStep? Master(string nodeId) => _masterSteps.GetValueOrDefault(MainStep(nodeId));

    public string PhaseName(FlowNode n)
        => Graph.Phases.FirstOrDefault(p => p.Id == n.PhaseId)?.Name
           ?? Graph.Subflows.FirstOrDefault(s => s.Id == n.SubflowId)?.Name ?? "";

    public string InstrumentName(string? code)
        => code is not null && Catalog.TryGetValue(code, out var c) ? c.Name : code ?? "";

    public bool IsSuture(string? code)
        => code is not null && Catalog.TryGetValue(code, out var c) && c.Category == "suture";

    // "43a" → 43, "47f" → 47: kayıtlarda ve AI'da ana adım numarası kullanılır.
    public static int MainStep(string? nodeId)
    {
        if (nodeId is null) return 0;
        var m = Regex.Match(nodeId, @"^\d+");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    public Dictionary<string, JsonElement> DefaultParameters()
        => new(Graph.ParameterDefaults);

    public NodeExpectation Expect(string nodeId, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var n = _nodes[nodeId];
        if (n.Kind == "preop")
            return new NodeExpectation { Kind = "checklist", Checklist = Checklists.GetValueOrDefault(nodeId) ?? new() };
        var accepted = new List<string>(n.Accepted);
        (string, string, List<string>)? byParameter = null;
        foreach (var rule in n.ParameterRules)
        {
            var v = StringParam(parameters, rule.Parameter);
            if (v is not null && rule.ExpectedInstruments.TryGetValue(v, out var list))
                byParameter = (rule.Parameter, v, list);
        }

        var clamps = new List<ClampNeed>();
        if (n.DynamicQuantity is { } dq)
        {
            foreach (var caliber in Calibers(parameters, dq.Parameter))
            {
                var code = dq.InstrumentByCaliber[caliber];
                var need = clamps.FirstOrDefault(c => c.Code == code);
                if (need is null) clamps.Add(new ClampNeed { Code = code, Qty = 1 });
                else need.Qty++;
            }
            var hemostats = dq.InstrumentByCaliber.Values.ToHashSet();
            accepted = accepted.Where(x => !hemostats.Contains(x)).Concat(clamps.Select(c => c.Code)).ToList();
        }

        // Teslim edilen iplikler: kabul listesindekiler + adımda verilen (PRIMARY/SUPPORT/ALTERNATIVE) iplikler.
        var usage = _instrumentUsage.GetValueOrDefault(nodeId) ?? new();
        var sutures = accepted.Where(IsSuture)
            .Concat(usage.Where(u => DeliveredUsages.Contains(u.Usage) && IsSuture(u.Code)).Select(u => u.Code))
            .Where(c => c != SurgeonChoiceLigature).Distinct().ToList();
        var deliverable = accepted.Where(c => c != "HAND" && !IsSuture(c)).ToList();
        // Klemp tipi damar çapına bağlıysa (23) hemostatik klempler yalnızca damar sayısı kadar, beklenen tipte kabul edilir.
        var hemostatsByVessel = n.DynamicQuantity?.InstrumentByCaliber.Values.ToHashSet() ?? new HashSet<string>();
        var companions = usage.Where(u => DeliveredUsages.Contains(u.Usage) && u.Code != "HAND" && !IsSuture(u.Code))
            .Select(u => u.Code).Where(c => !hemostatsByVessel.Contains(c)).Except(deliverable).Distinct().ToList();

        // Parametre kuralı (ör. kilolu hasta, estetik kapatma) seçilmeyen seçenekleri dışarıda bırakır.
        if (byParameter is { } bp)
        {
            var rule = n.ParameterRules.First(r => r.Parameter == bp.Item1);
            var excluded = rule.ExpectedInstruments.Values.SelectMany(v => v).Except(bp.Item3).ToHashSet();
            sutures = sutures.Where(c => !excluded.Contains(c)).ToList();
            deliverable = deliverable.Where(c => !excluded.Contains(c)).ToList();
            companions = companions.Where(c => !excluded.Contains(c)).ToList();
        }

        var countCategory = n.Action == "COUNT" && n.SubflowId is null ? CountCategoryOf(n.Label) : null;
        var kind = countCategory is not null ? "count"
            : deliverable.Count > 0 || sutures.Count > 0 ? "delivery"
            : "confirm";

        return new NodeExpectation
        {
            Accepted = accepted,
            ByParameter = byParameter,
            Clamps = clamps,
            Kind = kind,
            Deliverable = deliverable,
            Sutures = sutures,
            Companions = deliverable.Count > 0 || sutures.Count > 0 ? companions : new(),
            CountCategory = countCategory
        };
    }

    // Hekim tercihi: modeldeki SURGEON varyantları (variants[].instruments). Her grup bir karar, her seçenek eşdeğer
    // aletlerin listesidir (ör. Adım 8: [Stille, DeBakey] ya da [Crile eğri]). Hasta parametresiyle bu adımda kabul
    // edilmeyen seçenekler düşer; iki seçenekten azı kalırsa karar yoktur. Varyantı olmayan adımda her alternatif doğrudur.
    public List<List<List<string>>> ChoiceGroups(string nodeId, NodeExpectation x)
    {
        var groups = new List<List<List<string>>>();
        if (_nodes[nodeId].DynamicQuantity is not null || nodeId != MainStep(nodeId).ToString()) return groups;
        if (_masterSteps.GetValueOrDefault(MainStep(nodeId)) is not { } master) return groups;
        foreach (var v in master.Variants.Where(v => v.DecidedBy == "SURGEON" && v.Instruments.Count > 0))
        {
            var options = v.Instruments
                .Select(o => o.Where(c => x.Deliverable.Contains(c) || x.Sutures.Contains(c)).ToList())
                .Where(o => o.Count > 0).ToList();
            if (options.Count >= 2) groups.Add(options);
        }
        return groups;
    }

    // Ameliyat öncesi bölümün kontrol listesi, en temiz kaynaktan: alt maddeler (PRE01, PRE09) → yapılandırılmış hata
    // listesi (PRE03, 07, 08) → görevler. PDF'ten bölünmüş görev satırları birleştirilir, büyük harfli başlıklar atlanır.
    private static List<ChecklistItem> BuildChecklist(JsonElement section)
    {
        var id = section.GetProperty("id").GetString()!;
        if (section.TryGetProperty("substeps", out var subs))
            return subs.EnumerateArray().Select(s => new ChecklistItem
            {
                Id = s.GetProperty("id").GetString()!,
                Name = s.GetProperty("name").GetString()!,
                Task = s.TryGetProperty("task", out var t) ? t.GetString() : null,
                Optional = s.TryGetProperty("isConditional", out var c) && c.ValueKind == JsonValueKind.True,
                // Riskler yalnız "atlanırsa" alanlarından (görev metni risk değildir).
                Risks = new[] { "omissionRisks", "forms" }.Where(k => s.TryGetProperty(k, out _))
                    .SelectMany(k => Strings(s.GetProperty(k))).Take(6).ToList()
            }).ToList();

        if (section.TryGetProperty("errorChecklist", out var checks))
            return checks.EnumerateArray().Select((e, i) => new ChecklistItem
            {
                Id = $"{id}.{i + 1}",
                Name = e.GetProperty("task").GetString()!,
                Risks = Strings(e).Skip(1).Take(6).ToList()   // ilk metin görevin kendisi
            }).ToList();

        var risks = section.TryGetProperty("omissionRisks", out var r) ? Strings(r).Take(6).ToList() : new List<string>();
        var tasks = new List<string>();
        foreach (var line in section.GetProperty("tasks").EnumerateArray().Select(x => x.GetString()!.Trim()))
        {
            if (line.Length > 3 && line.Any(char.IsLetter) && line == line.ToUpper(Tr)) continue;   // "GENEL ANESTEZİ UYGULAMASINDA"
            if (tasks.Count > 0 && line.Length > 0 && char.IsLower(line[0]) && !tasks[^1].EndsWith('.')) tasks[^1] += " " + line;
            else tasks.Add(line);
        }
        return tasks.Select((t, i) => new ChecklistItem { Id = $"{id}.{i + 1}", Name = t, Risks = risks }).ToList();
    }

    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    // Risk metinlerini iç içe yapıdan düz listeye çıkarır (dizgiler; nesnelerde item/form/error ve alt listeler).
    private static IEnumerable<string> Strings(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var s = e.GetString()!.Trim();
                if (s.Length >= 4) yield return s;   // PDF kırpıntıları ("ise") atlanır
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) foreach (var y in Strings(x)) yield return y;
                break;
            case JsonValueKind.Object:
                foreach (var key in new[] { "task", "item", "form", "error", "omissionRisks", "consequences", "complications", "items", "forms", "errors" })
                    if (e.TryGetProperty(key, out var v)) foreach (var y in Strings(v)) yield return y;
                break;
        }
    }

    private static string? CountCategoryOf(string label)
    {
        var l = label.ToLower(new System.Globalization.CultureInfo("tr-TR"));
        return l.Contains("kompres") ? "COMPRESS" : l.Contains("alet") ? "INSTRUMENT" : l.Contains("iğne") ? "NEEDLE" : null;
    }

    public static string? StringParam(IReadOnlyDictionary<string, JsonElement> p, string key)
        => p.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IEnumerable<string> Calibers(IReadOnlyDictionary<string, JsonElement> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in v.EnumerateArray())
            if (item.TryGetProperty("caliber", out var c) && c.GetString() is { } s)
                yield return s;
    }

    private static T Load<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Akış modeli dosyası bulunamadı: {path}");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts)
               ?? throw new InvalidOperationException($"Dosya okunamadı: {path}");
    }
}
