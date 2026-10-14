using System.Globalization;
using System.Text.RegularExpressions;
using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// 60 adımlık modelde bir sapmanın deterministik klinik metni (AI'a gerek kalmadan, anında).
// Yanlış verilen aletin adı adımın onaylı hata senaryolarının başlıklarında aranır (ai-gateway knowledge.match_scenario'nun sade hâli);
// senaryo yoksa sapma türünün genel klinik metni (clinical-knowledge.json "*") kullanılır.
public sealed class FlowClinicalText
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private static readonly HashSet<string> Inactive = new() { "SUPERSEDED", "DRAFT" };
    private static readonly HashSet<string> Stopwords = new() { "iğneli", "cm", "no", "ile", "ve", "veya", "için", "set", "seti", "adet" };
    private static readonly string[] Separators = { " yerine ", " istendiği hâlde ", " istendiği halde " };
    // Genel başlıklarda ("yanlış sütür", "farklı tipte klemp") verilen alet adıyla değil türüyle anılır.
    private static readonly Dictionary<string, HashSet<string>> CategoryWords = new()
    {
        ["suture"] = new() { "sütür", "iplik" }, ["clamp"] = new() { "klemp" }, ["forceps"] = new() { "penset", "pens" },
        ["scissors"] = new() { "makas" }, ["blade"] = new() { "bistüri" }, ["retractor"] = new() { "ekartör" },
        ["antiseptic"] = new() { "solüsyon" }, ["fluid"] = new() { "solüsyon" }
    };
    // Yanlış seçim senaryosunun başlık işaretleri; "bistüri ucunun sapa takılmaması" gibi montaj/kontrol hataları seçime bağlanmaz.
    private static readonly string[] ChoiceMarkers = { " yerine ", " istendiği hâlde ", " istendiği halde ", "gerekmediği hâlde", "gerekmediği halde", "farklı" };

    private readonly FlowModelStore _model;
    private readonly ProcedureStore _generic;

    // Alet "markaları": katalog adlarının ilk kelimesi (crile, allis, babcock, metzenbaum…). Başlık uzatılandan başka
    // bir aleti adıyla anıyorsa o senaryo bu hatayı anlatmıyordur.
    private readonly HashSet<string> _brands;

    public FlowClinicalText(FlowModelStore model, ProcedureStore generic)
    {
        _model = model;
        _generic = generic;
        _brands = model.Catalog.Values
            .Select(c => Regex.Match(c.Name.ToLower(Tr), "[a-zçğıöşü]{4,}").Value)
            .Where(w => w.Length > 0 && !Stopwords.Contains(w) && !GenericWords.Contains(w)).ToHashSet();
    }

    private static readonly HashSet<string> GenericWords = new() { "dişli", "steril", "radyopak", "yıkama", "izotonik", "kullanılmış", "hasta", "çelik", "klorheksidinli" };

    public ClinicalKnowledgeEntry Entry(string nodeId, string deviationType, string? actual, IReadOnlyCollection<string>? wanted = null)
    {
        if (deviationType == "CHECKLIST_INCOMPLETE")
            return ChecklistEntry(nodeId, wanted ?? Array.Empty<string>());
        var generic = _generic.GetClinicalEntry(0, deviationType);
        var severity = generic?.Severity ?? "MEDIUM";
        var scenario = deviationType == "WRONG_INSTRUMENT" ? Match(nodeId, actual, wanted) : null;
        var names = string.Join(", ", (wanted ?? Array.Empty<string>()).Select(_model.InstrumentName));

        // Cerrah istemi hatası: verilen alet bu adımda kabul edilen bir alternatif, ama cerrah başkasını istedi.
        var acceptedHere = _model.Node(nodeId)?.Accepted.Contains(actual ?? "") == true;
        if (scenario is null && acceptedHere && names.Length > 0)
        {
            return new ClinicalKnowledgeEntry
            {
                StepId = nodeId,
                DeviationType = deviationType,
                PossibleRisk = "Cerrahın planladığı teknikten sapma ve gecikme; yanlış alet sahada kullanılırsa doku hasarı.",
                Explanation = $"Cerrah {names} istedi; {_model.InstrumentName(actual)} uzatıldı. Bu adımda iki seçenek de kullanılabilir, ancak seçim cerrahındır.",
                RecommendedAction = "İstem sesli olarak tekrar edilip doğrulanır; uzatılan alet geri alınır ve istenen alet uzatılır.",
                Severity = severity,
                Source = "cerrah istemi"
            };
        }
        if (scenario is not null)
        {
            return new ClinicalKnowledgeEntry
            {
                StepId = nodeId,
                DeviationType = deviationType,
                PossibleRisk = scenario.PossibleComplications.FirstOrDefault() ?? generic?.PossibleRisk ?? "",
                Explanation = scenario.ScrubError,
                RecommendedAction = PickAction(scenario.ResolutionSteps, wanted, actual),
                Severity = severity,
                Source = $"open-appendectomy@{_model.Version}:{scenario.Id}"
            };
        }

        var practice = _model.Master(nodeId)?.Scrub.CorrectPractice ?? "";
        return new ClinicalKnowledgeEntry
        {
            StepId = nodeId,
            DeviationType = deviationType,
            PossibleRisk = generic?.PossibleRisk ?? "Prosedür kuralına aykırı bir işlem tespit edildi.",
            Explanation = generic?.Explanation
                          ?? (names.Length > 0 ? $"Bu adımda beklenen: {names}." : "Bu adımda farklı bir uygulama bekleniyordu."),
            RecommendedAction = generic?.RecommendedAction ?? FirstSentence(practice),
            Severity = severity,
            Source = generic?.Source
        };
    }

    // Ameliyat öncesi kontrol listesinde atlanan madde: risk ve görev modelin kendi metninden.
    private ClinicalKnowledgeEntry ChecklistEntry(string nodeId, IReadOnlyCollection<string> missingIds)
    {
        var items = (_model.Checklists.GetValueOrDefault(nodeId) ?? new()).Where(i => missingIds.Contains(i.Id)).ToList();
        var first = items.FirstOrDefault();
        return new ClinicalKnowledgeEntry
        {
            StepId = nodeId,
            DeviationType = "CHECKLIST_INCOMPLETE",
            PossibleRisk = first?.Risks.FirstOrDefault() ?? "Hazırlık eksik kalır; hasta güvenliği ve ameliyat akışı etkilenebilir.",
            Explanation = $"Atlanan madde: {string.Join("; ", items.Select(i => i.Name))}.",
            RecommendedAction = first?.Task ?? (first is null ? "Kontrol listesini eksiksiz tamamlayın." : $"{first.Name} yapılmadan devam edilmez."),
            Severity = "HIGH",
            Source = $"open-appendectomy@{_model.Version}:preoperative:{nodeId}"
        };
    }

    public ErrorScenario? Match(string nodeId, string? actual, IReadOnlyCollection<string>? wanted)
    {
        if (actual is null || !_model.Catalog.ContainsKey(actual)) return null;
        var scenarios = _model.Master(nodeId)?.Scrub.ErrorScenarios.Where(e => !Inactive.Contains(e.ReviewStatus));
        if (scenarios is null) return null;

        var bad = WordsOf(actual);
        var badNumbers = Numbers(_model.InstrumentName(actual));
        var kind = CategoryWords.GetValueOrDefault(_model.Catalog[actual].Category) ?? new HashSet<string>();
        var good = new HashSet<string>((wanted ?? Array.Empty<string>()).SelectMany(WordsOf));
        ErrorScenario? best = null;
        var bestScore = 0;
        foreach (var e in scenarios)
        {
            var title = e.Title.ToLower(Tr);
            if (!ChoiceMarkers.Any(title.Contains)) continue;
            string left = "", right = title;
            foreach (var sep in Separators)
            {
                var i = title.IndexOf(sep, StringComparison.Ordinal);
                if (i < 0) continue;
                left = title[..i];
                right = title[(i + sep.Length)..];
                break;
            }
            var lw = Tokens(left);
            var rw = Tokens(right);
            var body = Tokens(e.ScrubError);
            if (!bad.Overlaps(rw) && !kind.Overlaps(rw)) continue;   // uzatılan alet (adı ya da türü) başlığın "verilen" tarafında geçmeli
            // Başlıkta verilen aletin numarası geçiyorsa (11 numaralı bistüri, 15 cm Allis, 2/0 Prolen) uzatılanınkiyle uyuşmalı.
            var titleNumbers = Numbers(right);
            if (titleNumbers.Count > 0 && !titleNumbers.Overlaps(badNumbers)) continue;
            var otherBrands = rw.Count(w => _brands.Contains(w) && !bad.Contains(w));
            var score = 3 * bad.Intersect(rw).Count() + 2 * good.Intersect(lw).Count()
                        - 2 * bad.Intersect(lw).Count() + bad.Intersect(body).Count()
                        + (titleNumbers.Count > 0 ? 3 : 0) - 4 * otherBrands;
            if (score > bestScore)
            {
                best = e;
                bestScore = score;
            }
        }
        return best;
    }

    private string PickAction(List<string> steps, IReadOnlyCollection<string>? wanted, string? actual)
    {
        if (steps.Count == 0) return "";
        var good = new HashSet<string>((wanted ?? Array.Empty<string>()).SelectMany(WordsOf));
        var bad = actual is null ? new HashSet<string>() : WordsOf(actual);
        var best = steps[0];
        var bestScore = 0;
        foreach (var s in steps)
        {
            var w = Tokens(s);
            var score = good.Intersect(w).Count() - bad.Intersect(w).Count();
            if (score > bestScore)
            {
                best = s;
                bestScore = score;
            }
        }
        return best;
    }

    private HashSet<string> WordsOf(string code)
    {
        var words = Tokens(_model.InstrumentName(code));
        if (_model.Descriptors.TryGetValue(code, out var extra)) words.UnionWith(extra);
        return words;
    }

    private static HashSet<string> Tokens(string text)
    {
        var t = text.ToLower(Tr);
        var words = Regex.Matches(t, "[a-zçğıöşü]+").Select(m => m.Value).Where(w => w.Length > 2 && !Stopwords.Contains(w));
        return words.Concat(Regex.Matches(t, @"\d/0").Select(m => m.Value)).ToHashSet();
    }

    private static HashSet<string> Numbers(string text)
        => Regex.Matches(text, @"\d+/\d+|\d+").Select(m => m.Value).ToHashSet();

    private static string FirstSentence(string text)
    {
        var i = text.IndexOf(". ", StringComparison.Ordinal);
        return i < 0 ? text : text[..(i + 1)];
    }
}
