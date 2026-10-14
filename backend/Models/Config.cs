using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arqenion.Api.Models;

// ----- Procedure definition (loaded from Procedures/open-appendectomy-v1.json) -----

public sealed class ProcedureDefinition
{
    public string ProcedureCode { get; set; } = "";
    public string Version { get; set; } = "";
    public string Role { get; set; } = "";
    public string Title { get; set; } = "";
    public int StartingScore { get; set; } = 100;
    public ScoreBounds ScoreBounds { get; set; } = new();
    public List<string> DeviationTypes { get; set; } = new();
    public List<ProcedureStep> Steps { get; set; } = new();
    public List<ComplicationDefinition> Complications { get; set; } = new();
    public List<InstrumentItem> InstrumentCatalog { get; set; } = new();
    public List<SutureItem> SutureCatalog { get; set; } = new();
}

public sealed class ScoreBounds
{
    public int Min { get; set; } = 0;
    public int Max { get; set; } = 100;
}

public sealed class ProcedureStep
{
    public int StepId { get; set; }
    public List<int> SourceSteps { get; set; } = new();
    // Kaynak dokümandaki 22 ana başlığın sırası; birden fazla adım aynı başlığı paylaşabilir.
    public int PhaseNo { get; set; }
    public string Phase { get; set; } = "";
    public string Title { get; set; } = "";
    public string ExpectedEvent { get; set; } = "";
    public string? Target { get; set; }
    public List<string> AllowedInstruments { get; set; } = new();
    public string? RequiredSuture { get; set; }
    public List<string>? CountTargets { get; set; }
    public Dictionary<string, int>? ExpectedCounts { get; set; }
    public int SuccessScore { get; set; }
    public Dictionary<string, int> Penalties { get; set; } = new();
    public string? ClinicalNote { get; set; }
    public int? NextStepId { get; set; }
}

// Belirli bir adımda belirli bir yanlış alet/sütür seçiminin doğurduğu klinik komplikasyon
// ve teknikerin bunu gidermek için sırayla teslim etmesi gereken malzemeler.
public sealed class ComplicationDefinition
{
    public string ComplicationId { get; set; } = "";
    public int StepId { get; set; }
    public List<int> SourceSteps { get; set; } = new();
    public string Title { get; set; } = "";
    // Tetikleyici seçim: bu aletlerden/sütürlerden biri teslim edilirse komplikasyon açılır.
    public List<string> TriggerInstruments { get; set; } = new();
    public List<string> TriggerSutures { get; set; } = new();
    // Kırmızı: ne oldu.
    public string Narrative { get; set; } = "";
    public string Severity { get; set; } = "HIGH";
    public int Penalty { get; set; } = 12;
    // Teknikerin gidereceği bir komplikasyon mu? false ise yalnızca kayda geçer (ör. proflaksi anestezinin işi).
    public bool ScrubResolvable { get; set; } = true;
    // Yeşil: çözüm özeti.
    public string ResolutionNote { get; set; } = "";
    public List<ResolutionStep> Resolution { get; set; } = new();
    public int ResolutionScore { get; set; } = 4;
    public int MisstepPenalty { get; set; } = 6;
}

// Komplikasyon çözüm zincirindeki tek bir malzeme teslimi.
public sealed class ResolutionStep
{
    public string Title { get; set; } = "";
    public string ExpectedEvent { get; set; } = "instrument_delivered";
    public string? Target { get; set; } = "SURGEON";
    public List<string> AllowedInstruments { get; set; } = new();
    public string? RequiredSuture { get; set; }
    public string? Note { get; set; }
}

public sealed class InstrumentItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Sterile { get; set; }
    // Karaoğlu Genel Cerrahi Apendektomi Seti ürün kodu; sette olmayan sarf/ek malzemelerde null.
    public string? SetCode { get; set; }
    public int Quantity { get; set; } = 1;
    public bool InSet { get; set; } = true;
    public string Category { get; set; } = "";
}

public sealed class SutureItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

// ----- Scenarios (loaded from Procedures/scenarios.json) -----

public sealed class ScenariosFile
{
    public List<Scenario> Scenarios { get; set; } = new();
}

public sealed class Scenario
{
    public string ScenarioId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string ProcedureCode { get; set; } = "";
    public PatientProfile Patient { get; set; } = new();
    public List<string> EnabledErrors { get; set; } = new();
    public int StartingScore { get; set; } = 100;
    public string? Description { get; set; }

    // ----- 60 adımlık model (v2.4); eski model bu alanları yok sayar -----
    // Koşul planı: true/false ya da ardışık değerlendirmeler için liste (ör. "BLEEDING_AFTER_IRRIGATION": [true, false]).
    public Dictionary<string, JsonElement> Conditions { get; set; } = new();
    // Hasta parametreleri (ör. SKIN_CLOSURE_TYPE, MESO_VESSELS); PATIENT_BODY_TYPE patient.bodyType'tan gelir.
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();
    // Başlangıç sayım kaydı; COUNT_MISMATCH planlıysa MissingItem kategorisinden bir adet masada eksik olur.
    public Dictionary<string, int>? Counts { get; set; }
    public string MissingItem { get; set; } = "COMPRESS";
    // Hataya göre dal: "errors" (varsayılan; Procedures/adaptive-rules.json) ya da "off" (sınav modu, yalnız senaryo planı).
    public string AdaptiveMode { get; set; } = "errors";
    // Cerrah istemi: "random" (varsayılan; seans başında belirlenir) ya da "off" (her kabul edilen alternatif doğru sayılır).
    public string SurgeonRequestMode { get; set; } = "random";
    // Belirli düğümlerde cerrahın istemini sabitler (ör. "51": "CLAMP_OCHSNER_KOCHER_CURVED_20").
    public Dictionary<string, string> SurgeonRequests { get; set; } = new();
    // Seans ameliyat öncesi bölümlerle (PRE01–PRE09 kontrol listeleri) başlar.
    public bool IncludePreop { get; set; }
}

public sealed class PatientProfile
{
    public string BodyType { get; set; } = "NORMAL";
    public bool HasAdhesion { get; set; }
}

// ----- Clinical knowledge table (loaded from Procedures/clinical-knowledge.json) -----

public sealed class ClinicalKnowledgeFile
{
    public List<ClinicalKnowledgeEntry> Entries { get; set; } = new();
}

public sealed class ClinicalKnowledgeEntry
{
    // stepId may be an int or the wildcard string "*" in JSON.
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string StepId { get; set; } = "*";
    public string DeviationType { get; set; } = "";
    public string PossibleRisk { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public string Severity { get; set; } = "MEDIUM";
    public string? Source { get; set; }
}

public sealed class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Number
            ? reader.GetInt32().ToString()
            : reader.GetString() ?? "*";

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
