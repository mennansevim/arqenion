namespace Arqenion.Api.Models;

// ----- API request/response DTOs (contract shared with simulator & VR) -----

public sealed class StartSessionRequest
{
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
}

public sealed class StepView
{
    // 60 adımlık modelde ana adım numarası (alt akış "47b" → 47); tam düğüm NodeId'dedir.
    public int StepId { get; set; }
    public int PhaseNo { get; set; }
    public string Title { get; set; } = "";
    public string Phase { get; set; } = "";
    public string ExpectedEvent { get; set; } = "";
    public List<string> AllowedInstruments { get; set; } = new();
    public string? RequiredSuture { get; set; }
    public string? Target { get; set; }

    // ----- 60 adımlık model (v2.4) alanları; eski modelde boş kalır -----
    public string? NodeId { get; set; }
    public string? Kind { get; set; }                       // delivery | confirm | count
    public List<string>? RequiredSutures { get; set; }
    public List<ClampNeed>? Clamps { get; set; }            // damar sayısına bağlı klempler (kalan)
    public string? CountCategory { get; set; }
    public int? TableCount { get; set; }                    // masada sayılacak gerçek adet
    public int? RecordedCount { get; set; }                 // başlangıç sayım kaydı
    public string? Condition { get; set; }                  // koşullu/alt akış düğümünün koşulu
    public string? ConditionText { get; set; }
    public bool IsBranch { get; set; }
    public string? Instruction { get; set; }
    public string? ClinicalNote { get; set; }
    public string? SurgeonRequest { get; set; }             // hekim tercihine bırakılan adımda cerrahın istemi
}

// Açık bir komplikasyonun anlık durumu: ne oldu, çözüm zincirinde nerede kalındı, şimdi ne bekleniyor.
public sealed class ActiveComplicationDto
{
    public string ComplicationId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Narrative { get; set; } = "";
    public string ResolutionNote { get; set; } = "";
    public string Severity { get; set; } = "HIGH";
    public int StepId { get; set; }
    public bool ScrubResolvable { get; set; } = true;
    public int ResolutionIndex { get; set; }
    public int ResolutionTotal { get; set; }
    public bool Resolved { get; set; }
    // Çözüm zincirinde şu an beklenen malzeme teslimi (çözüm bittiyse null).
    public ResolutionStepView? CurrentAction { get; set; }
    public List<ResolutionStepView> Actions { get; set; } = new();
}

public sealed class ResolutionStepView
{
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string ExpectedEvent { get; set; } = "";
    public string? Target { get; set; }
    public List<string> AllowedInstruments { get; set; } = new();
    public string? RequiredSuture { get; set; }
    public string? Note { get; set; }
    public bool Done { get; set; }
}

public sealed class StartSessionResponse
{
    public string SessionId { get; set; } = "";
    public string ScenarioName { get; set; } = "";
    public string ProcedureCode { get; set; } = "";
    public StepView? CurrentStep { get; set; }
    public int Score { get; set; }
    public string Status { get; set; } = "IN_PROGRESS";
    public ActiveComplicationDto? ActiveComplication { get; set; }
    public string ProcedureModel { get; set; } = "v1";
}

public sealed class EventRequest
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; } = "";
    public string? InstrumentCode { get; set; }
    public string? SutureCode { get; set; }
    public string? Target { get; set; }
    public Dictionary<string, int>? Counts { get; set; }
    public DateTime? Timestamp { get; set; }
}

public sealed class DeviationDto
{
    public string DeviationType { get; set; } = "";
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string Severity { get; set; } = "MEDIUM";
}

public sealed class ValidationResponse
{
    public bool Success { get; set; }
    public int CurrentStepId { get; set; }
    public int? NextStepId { get; set; }
    public int Score { get; set; }
    public DeviationDto? Deviation { get; set; }
    public string Message { get; set; } = "";
    public bool AllowRetry { get; set; }
    public bool Completed { get; set; }
    public StepView? NextStep { get; set; }
    // Deterministik komplikasyon açıklaması (klinik tablodan); SignalR'a bağlı kalmadan anında döner.
    public AiFeedbackDto? Complication { get; set; }
    // Ardışık/tekrar eden hatalardan üretilen kümülatif komplikasyon değerlendirmesi.
    public CumulativeComplicationDto? CumulativeComplication { get; set; }
    // Malzeme seçimiyle tetiklenen senaryo komplikasyonu ve çözüm zincirinin anlık durumu.
    public ActiveComplicationDto? ActiveComplication { get; set; }
    // 60 adımlık model: düğüm kimlikleri ve karar noktasında girilen/kapanan dal.
    public string? CurrentNodeId { get; set; }
    public string? NextNodeId { get; set; }
    public FlowBranchDto? Branch { get; set; }
}

public sealed class ComplicationItemDto
{
    public string DeviationType { get; set; } = "";
    public string Risk { get; set; } = "";
    public int Occurrences { get; set; }
    public int Probability { get; set; }
    public string Severity { get; set; } = "MEDIUM";
    public bool Primary { get; set; }
}

public sealed class CumulativeComplicationDto
{
    public int Streak { get; set; }
    public string Level { get; set; } = "MEDIUM";
    public int RiskScore { get; set; }
    public string Summary { get; set; } = "";
    public List<ComplicationItemDto> Items { get; set; } = new();
}

public sealed class ReportResponse
{
    public int Score { get; set; }
    public int SuccessRate { get; set; }
    public int CorrectActions { get; set; }
    public int WrongActions { get; set; }
    public int SterileViolations { get; set; }
    public int HintsUsed { get; set; }
    public int ComplicationsTriggered { get; set; }
    public int ComplicationsResolved { get; set; }
    public string Summary { get; set; } = "";
}

public sealed class AiFeedbackDto
{
    public string SessionId { get; set; } = "";
    public string EventId { get; set; } = "";
    public int StepId { get; set; }
    public string DeviationType { get; set; } = "";
    public string PossibleRisk { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public string Severity { get; set; } = "MEDIUM";
    public string Source { get; set; } = "table";
    public string? ModelName { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

public sealed class ScoreUpdateDto
{
    public string SessionId { get; set; } = "";
    public int Score { get; set; }
}
