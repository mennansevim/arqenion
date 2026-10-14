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

    public string ExpectedEvent => Kind switch
    {
        "delivery" => "instrument_delivered",
        "count" => "count_confirmed",
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

    public FlowModelStore(string modelDir, string version, string scoringPath, string? descriptorsPath = null)
    {
        var baseName = Path.Combine(modelDir, $"open-appendectomy-v{version}");
        Graph = Load<FlowGraphFile>($"{baseName}.graph.json");
        var master = Load<MasterModelFile>($"{baseName}.json");
        Scoring = Load<ScoringConfig>(scoringPath);

        Version = Graph.Version;
        Catalog = master.Catalogs.Instruments;
        if (descriptorsPath is not null && File.Exists(descriptorsPath))
            Descriptors = Load<DescriptorsFile>(descriptorsPath).Descriptors;
        _nodes = Graph.Nodes.ToDictionary(n => n.Id);
        _masterSteps = master.Steps.ToDictionary(s => s.Step);
        foreach (var s in master.Steps) _instrumentUsage[s.Step.ToString()] = s.Instruments;
        foreach (var s in master.Subflows.SelectMany(f => f.Steps)) _instrumentUsage[s.Id] = s.Instruments;

        PlayableNodes = Graph.Nodes.Where(n => n.Kind != "preop")
            .OrderBy(n => MainStep(n.Id)).ThenBy(n => n.Id.Length).ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
        MainStepCount = PlayableNodes.Count(n => n.SubflowId is null);
        PhaseNo = Graph.Phases.Select((p, i) => (p.Id, i + 1)).ToDictionary(x => x.Id, x => x.Item2);
    }

    public FlowGraphFile Graph { get; }
    public string Version { get; }
    public ScoringConfig Scoring { get; }
    public Dictionary<string, CatalogInstrument> Catalog { get; }
    public Dictionary<string, List<string>> Descriptors { get; private set; } = new();
    public IReadOnlyList<FlowNode> PlayableNodes { get; }
    public int MainStepCount { get; }
    public Dictionary<string, int> PhaseNo { get; }
    public string FirstNodeId => "1";   // ameliyat öncesi (PRE01–09) hazırlık bölümleri henüz oynanmıyor

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
