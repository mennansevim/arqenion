using System.ComponentModel.DataAnnotations;

namespace Arqenion.Api.Models;

// ----- Persisted entities -----

public sealed class User
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    // Giriş: kullanıcı adı + PBKDF2 şifre özeti. Eski kayıtlarda (kimlik doğrulama öncesi) boştur; bu hesaplar giriş yapamaz.
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public string Role { get; set; } = Roles.Trainee;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class Roles
{
    public const string Trainee = "trainee";        // kendi seanslarını açar ve oynar
    public const string Instructor = "instructor";  // kullanıcı açar, bütün seansları ve hata bildirimlerini görür
    public static readonly string[] All = { Trainee, Instructor };
}

public sealed class SessionRecord
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string ProcedureCode { get; set; } = "";
    public int? CurrentStepId { get; set; }
    public int Score { get; set; }
    public int RawScore { get; set; }
    public string Status { get; set; } = "IN_PROGRESS"; // IN_PROGRESS | COMPLETED | ABORTED
    public int CorrectActions { get; set; }
    public int WrongActions { get; set; }
    public int SterileViolations { get; set; }
    public int HintsUsed { get; set; }
    public int ConsecutiveErrors { get; set; }
    // Açık komplikasyon varsa olay doğrulaması çözüm zincirine yönlenir.
    public string? ActiveComplicationId { get; set; }
    public int ComplicationStepIndex { get; set; }
    public int ComplicationsTriggered { get; set; }
    public int ComplicationsResolved { get; set; }
    // Seansın başladığı model: null/"v1" eski 25 adımlı model, "v2.4" 60 adımlık akış modeli.
    public string? ProcedureModel { get; set; }
    // v2.4: bulunulan düğüm ("43a" gibi) ve akış durumu (koşul planı, döngü sayaçları, yol) JSON olarak.
    public string? CurrentNodeId { get; set; }
    public string? FlowStateJson { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class SessionEventRecord
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int StepId { get; set; }
    public string? NodeId { get; set; }
    public string? InstrumentCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? DeviationType { get; set; }
    public int ScoreChange { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Uzaktaki kullanıcının bildirdiği hata; backend/AppData/issues.json içinde saklanır.
public sealed class IssueRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public int Number { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN | RESOLVED
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Severity { get; set; } = "NORMAL"; // LOW | NORMAL | HIGH | BLOCKER
    public string? Area { get; set; }
    public string? ReportedBy { get; set; }
    // Bildirim anındaki simülatör durumu (adım, aşama, skor, seans, tarayıcı).
    public Dictionary<string, string> Context { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
}

public sealed class AiFeedbackRecord
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = "";
    public string? SessionEventId { get; set; }
    public int StepId { get; set; }
    public string? DeviationType { get; set; }
    public string PossibleRisk { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public string Severity { get; set; } = "MEDIUM";
    public string Source { get; set; } = "table";
    public string? ModelName { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
