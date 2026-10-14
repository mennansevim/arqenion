using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

public readonly record struct DeviationSample(int StepId, string DeviationType);

// Ardışık ve tekrar eden sapmalardan kümülatif komplikasyon değerlendirmesi üretir.
// Deterministik: her sapma tipinin risk metni klinik tablodan gelir; olasılık tekrar + ardışıklıkla artar.
public sealed class ComplicationEngine
{
    private readonly ProcedureStore _store;

    public ComplicationEngine(ProcedureStore store) => _store = store;

    // lookup: sapmanın klinik metni; verilmezse eski modelin klinik tablosu (60 adımlık model kendi metnini verir).
    public CumulativeComplicationDto Build(IReadOnlyList<DeviationSample> deviations, int streak,
        Func<int, string, ClinicalKnowledgeEntry?>? lookup = null)
    {
        lookup ??= _store.GetClinicalEntry;
        var items = deviations
            .GroupBy(d => d.DeviationType)
            .Select(g =>
            {
                var latest = g.Last();
                var entry = lookup(latest.StepId, g.Key);
                var occurrences = g.Count();
                var probability = Math.Clamp(BaseWeight(g.Key) + (occurrences - 1) * 15 + streak * 6, 20, 97);
                return new ComplicationItemDto
                {
                    DeviationType = g.Key,
                    Risk = entry?.PossibleRisk ?? "Prosedür kuralına aykırı bir işlem tespit edildi.",
                    Occurrences = occurrences,
                    Probability = probability,
                    Severity = entry?.Severity ?? "MEDIUM"
                };
            })
            .OrderByDescending(x => x.Probability)
            .ThenByDescending(x => x.Occurrences)
            .Take(5)
            .ToList();

        if (items.Count > 0) items[0].Primary = true;

        var level = streak >= 3 ? "CRITICAL"
            : streak == 2 ? "HIGH"
            : items.Count > 1 ? "ELEVATED"
            : "MEDIUM";

        var riskScore = items.Count == 0
            ? 0
            : Math.Clamp(items.Max(x => x.Probability) + (streak - 1) * 5, 0, 99);

        return new CumulativeComplicationDto
        {
            Streak = streak,
            Level = level,
            RiskScore = riskScore,
            Summary = BuildSummary(items, streak, level),
            Items = items
        };
    }

    private static int BaseWeight(string type) => type switch
    {
        "COUNT_MISMATCH" => 60,
        "STERILE_VIOLATION" => 55,
        "CONTAMINATED_TOOL_REUSE" => 52,
        "INSTRUMENT_DROPPED" => 40,
        "WRONG_INSTRUMENT" => 38,
        "WRONG_ORDER" => 30,
        _ => 35
    };

    private static string BuildSummary(List<ComplicationItemDto> items, int streak, string level)
    {
        if (items.Count == 0) return "Aktif komplikasyon riski yok.";
        var primary = items[0];
        var lvlTr = level switch
        {
            "CRITICAL" => "KRİTİK",
            "HIGH" => "YÜKSEK",
            "ELEVATED" => "ARTMIŞ",
            _ => "ORTA"
        };
        var streakTxt = streak >= 2 ? $"{streak} ardışık hata — " : "";
        return $"{streakTxt}kümülatif komplikasyon riski {lvlTr}. En olası: {primary.Risk} (%{primary.Probability}).";
    }
}
