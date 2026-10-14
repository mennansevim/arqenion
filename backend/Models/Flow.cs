using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arqenion.Api.Models;

// ----- 60 adımlık akış modeli (procedure-model/dist/open-appendectomy-v2.4.graph.json + ana model) -----
// Düğüm kimlikleri metindir: ana adımlar "1".."60", alt akış adımları "43a", "47a".."47f".

public sealed class FlowGraphFile
{
    public string Procedure { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, FlowCondition> Conditions { get; set; } = new();
    public Dictionary<string, JsonElement> ParameterDefaults { get; set; } = new();
    [JsonConverter(typeof(NodeIdConverter))] public string? Entry { get; set; }
    public List<FlowNode> Nodes { get; set; } = new();
    public List<FlowPhase> Phases { get; set; } = new();
    public List<FlowSubflowRef> Subflows { get; set; } = new();
}

public sealed class FlowCondition
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    // SCENARIO: senaryo belirler · ADAPTIVE: adaptif motor (şimdilik senaryo) · RUNTIME: kursiyerin olayları · OUTCOME: akışı değiştirmez
    public string Source { get; set; } = "";
    [JsonConverter(typeof(NodeIdConverter))] public string? EvaluatedAt { get; set; }
}

public sealed class FlowNode
{
    [JsonConverter(typeof(NodeIdConverter))] public string Id { get; set; } = "";
    public string Kind { get; set; } = "";              // step | conditional | subflow | preop
    public bool IsDecision { get; set; }
    public string Label { get; set; } = "";
    public string? PhaseId { get; set; }
    public string? SubflowId { get; set; }
    public string? Action { get; set; }
    public string? Instrument { get; set; }
    public string? Target { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool Mandatory { get; set; }
    public string? Condition { get; set; }
    public List<string> Accepted { get; set; } = new();
    public List<FlowParameterRule> ParameterRules { get; set; } = new();
    public FlowDynamicQuantity? DynamicQuantity { get; set; }
    public List<JsonElement> Pending { get; set; } = new();
    [JsonConverter(typeof(NodeIdConverter))] public string? Next { get; set; }
    public List<FlowBranch> Branches { get; set; } = new();
}

public sealed class FlowBranch
{
    public string When { get; set; } = "";
    [JsonConverter(typeof(NodeIdConverter))] public string GoTo { get; set; } = "";
    public string? Note { get; set; }
    public int? MaxIterations { get; set; }
}

public sealed class FlowParameterRule
{
    public string Parameter { get; set; } = "";
    public Dictionary<string, List<string>> ExpectedInstruments { get; set; } = new();
}

public sealed class FlowDynamicQuantity
{
    public string Parameter { get; set; } = "";
    public string? Rule { get; set; }
    public Dictionary<string, string> InstrumentByCaliber { get; set; } = new();
}

public sealed class FlowPhase
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class FlowSubflowRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

// Ana model (open-appendectomy-v2.4.json): alet adları, adım alet kullanımları, tekniker metni ve hata senaryoları.
public sealed class MasterModelFile
{
    public string SchemaVersion { get; set; } = "";
    public MasterCatalogs Catalogs { get; set; } = new();
    public List<MasterStep> Steps { get; set; } = new();
    public List<MasterSubflow> Subflows { get; set; } = new();
}

public sealed class MasterCatalogs
{
    public Dictionary<string, CatalogInstrument> Instruments { get; set; } = new();
}

public sealed class CatalogInstrument
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    // Yalnızca tepside duran, hiçbir adımda kabul edilmeyen alet (model kataloğu, clinical_review.py 6. tur).
    public bool Distractor { get; set; }
}

public sealed class DescriptorsFile
{
    // Alet adında geçmeyen niteleyiciler (ör. Ochsner-Kocher → "dişli"); klinik metin eşleştirmede kullanılır.
    public Dictionary<string, List<string>> Descriptors { get; set; } = new();
}

public sealed class MasterStep
{
    public int Step { get; set; }
    public string Name { get; set; } = "";
    public bool NewInstrumentHandover { get; set; }
    public List<StepInstrument> Instruments { get; set; } = new();
    // Cerrahın karar verdiği seçenekler; "instruments" doluysa cerrah istemi bu kodlardan belirlenir (ör. Adım 21).
    public List<StepVariant> Variants { get; set; } = new();
    public ScrubInfo Scrub { get; set; } = new();
}

public sealed class StepVariant
{
    public string Id { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    // Seçenek başına eşdeğer alet kodları, ör. [["FORCEPS_STILLE…", "FORCEPS_DEBAKEY"], ["CLAMP_CRILE_CURVED_14"]].
    public List<List<string>> Instruments { get; set; } = new();
}

public sealed class MasterSubflow
{
    public string Id { get; set; } = "";
    public List<MasterSubflowStep> Steps { get; set; } = new();
}

public sealed class MasterSubflowStep
{
    public string Id { get; set; } = "";
    public List<StepInstrument> Instruments { get; set; } = new();
}

public sealed class StepInstrument
{
    public string Code { get; set; } = "";
    public string Usage { get; set; } = "";   // PRIMARY | SUPPORT | ALTERNATIVE | IN_USE | STANDBY
}

public sealed class ScrubInfo
{
    public string CorrectPractice { get; set; } = "";
    public List<ErrorScenario> ErrorScenarios { get; set; } = new();
}

public sealed class ErrorScenario
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ScrubError { get; set; } = "";
    public List<string> PossibleComplications { get; set; } = new();
    public List<string> ResolutionSteps { get; set; } = new();
    public string ReviewStatus { get; set; } = "";
}

// ----- Puanlama (backend/Procedures/scoring.json) -----

public sealed class ScoringConfig
{
    public int StartingScore { get; set; } = 100;
    public ScoreBounds Bounds { get; set; } = new();
    // Adım türüne göre doğru işlem puanı: delivery (alet teslimi), confirm (onay), count (sayım).
    public Dictionary<string, int> Success { get; set; } = new();
    public int BranchStepBonus { get; set; }
    public int BranchResolvedBonus { get; set; }
    public int HintPenalty { get; set; } = 2;
    public int EscalationPerConsecutiveError { get; set; } = 3;
    public Dictionary<string, int> Penalties { get; set; } = new();
    public Dictionary<string, StepScoring> StepOverrides { get; set; } = new();
}

public sealed class StepScoring
{
    public int? Success { get; set; }
    public Dictionary<string, int> Penalties { get; set; } = new();
}

// ----- Seansın akış durumu (SessionRecord.FlowStateJson) -----

public sealed class FlowState
{
    // Koşul planı: her koşulun ardışık değerlendirmelerdeki değeri (Python walk() ile aynı anlam).
    public Dictionary<string, List<bool>> Conditions { get; set; } = new();
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();
    // Masadaki gerçek sayılar (senaryo eksik malzeme koyduysa başlangıç kaydından farklıdır).
    public Dictionary<string, int> Counts { get; set; } = new();
    public Dictionary<string, int> RecordedCounts { get; set; } = new();
    public Dictionary<string, int> Evaluations { get; set; } = new();   // "düğüm|koşul" → kaç kez değerlendirildi
    public Dictionary<string, int> Taken { get; set; } = new();         // "düğüm|koşul" → dal kaç kez alındı
    public List<string> Path { get; set; } = new();
    // Klemp adedi damar sayısına bağlı adımda (23) bu düğümde teslim edilenler.
    public Dictionary<string, int> Delivered { get; set; } = new();
    public OpenBranch? OpenBranch { get; set; }
    // Cerrah istemi: hekim tercihine bırakılan adımlarda cerrahın istediği alet/iplik (düğüm → kodlar, grup başına bir).
    public Dictionary<string, List<string>> Requests { get; set; } = new();
}

public sealed class OpenBranch
{
    public string Condition { get; set; } = "";
    public string Origin { get; set; } = "";
    public string? Resume { get; set; }
}

// Akış bir karar noktasında dala girdi ya da daldan ana akışa döndü.
public sealed class FlowBranchDto
{
    public string Condition { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string FromNodeId { get; set; } = "";
    public string? ToNodeId { get; set; }
    public bool Resolved { get; set; }
}

public sealed class ClampNeed
{
    public string Code { get; set; } = "";
    public int Qty { get; set; }
}

// JSON'da sayı (12) ya da metin ("43a") olabilen düğüm kimliği; null korunur.
public sealed class NodeIdConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetInt32().ToString(),
            _ => reader.GetString()
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
