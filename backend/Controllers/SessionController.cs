using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Arqenion.Api.Data;
using Arqenion.Api.Hubs;
using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public sealed class SessionController : ControllerBase
{
    private readonly ArqenionDbContext _db;
    private readonly ProcedureStore _store;
    private readonly ProcedureService _procedure;
    private readonly FlowModelStore _flowModel;
    private readonly FlowProcedureService _flow;
    private readonly FlowClinicalText _flowText;
    private readonly IConfiguration _config;
    private readonly ComplicationEngine _complications;
    private readonly IHubContext<SimulationHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionController> _logger;
    private readonly AssetStore _assets;

    public SessionController(ArqenionDbContext db, ProcedureStore store, ProcedureService procedure,
        FlowModelStore flowModel, FlowProcedureService flow, FlowClinicalText flowText, IConfiguration config,
        ComplicationEngine complications, IHubContext<SimulationHub> hub, IServiceScopeFactory scopeFactory,
        ILogger<SessionController> logger, AssetStore assets)
    {
        _assets = assets;
        _db = db;
        _store = store;
        _procedure = procedure;
        _flowModel = flowModel;
        _flow = flow;
        _flowText = flowText;
        _config = config;
        _complications = complications;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Yeni seansların başlayacağı model: "v2.4" (60 adım, varsayılan) ya da "v1" (eski 25 adım).
    private string ActiveModel => _config["Procedure:Model"] ?? FlowProcedureService.ModelKey;
    private static bool IsFlow(SessionRecord s) => s.ProcedureModel == FlowProcedureService.ModelKey;
    private static bool IsFlowModel(string model) => model == FlowProcedureService.ModelKey;

    // Simülatör/VR istemcisi adım listesini ve malzeme kataloğunu buradan okur; kopya tanım tutmaz.
    // ?model=v1 ya da ?model=v2.4 ile belirli bir model istenebilir; verilmezse yeni seansların modeli döner.
    [AllowAnonymous]
    [HttpGet("/api/procedure")]
    public ActionResult GetProcedure([FromQuery] string? model = null)
    {
        if (IsFlowModel(model ?? ActiveModel))
            return Ok(FlowProcedure());

        var p = _store.Procedure;
        return Ok(new
        {
            ProcedureModel = "v1",
            p.ProcedureCode,
            p.Version,
            p.Title,
            Steps = p.Steps.Select(s => new
            {
                s.StepId, s.PhaseNo, s.Phase, s.Title, s.ExpectedEvent, s.Target,
                s.AllowedInstruments, s.RequiredSuture, s.ExpectedCounts, s.ClinicalNote, s.SourceSteps
            }),
            PhaseCount = p.Steps.Select(s => s.PhaseNo).Distinct().Count(),
            p.InstrumentCatalog,
            p.SutureCatalog,
            Complications = p.Complications.Select(c => new
            {
                c.ComplicationId, c.StepId, c.Title, c.Severity, c.ScrubResolvable,
                c.TriggerInstruments, c.TriggerSutures, ResolutionCount = c.Resolution.Count
            })
        });
    }

    private object FlowProcedure()
    {
        var m = _flowModel;
        var preview = new FlowState { Parameters = m.DefaultParameters() };
        return new
        {
            ProcedureModel = FlowProcedureService.ModelKey,
            ProcedureCode = "OPEN_APPENDECTOMY",
            m.Version,
            Title = "Açık Apendektomi — Scrub Teknikeri",
            MainStepCount = m.MainStepCount,
            PhaseCount = m.Graph.Phases.Count,
            // Varsayılan hasta parametreleriyle; seansın kendi beklentisi StepView'da (currentStep/nextStep) gelir.
            Steps = m.PlayableNodes.Select(n => _flow.View(n.Id, preview)),
            InstrumentCatalog = m.Catalog.Where(c => c.Value.Category != "suture")
                .Select(c => new { Code = c.Key, c.Value.Name, c.Value.Category, Sterile = true, Asset = AssetRef(c.Key) }),
            SutureCatalog = m.Catalog.Where(c => c.Value.Category == "suture")
                .Select(c => new { Code = c.Key, c.Value.Name, Asset = AssetRef(c.Key) }),
            Conditions = m.Graph.Conditions.Values.Where(c => c.Source != "OUTCOME")
                .Select(c => new { c.Code, c.Description, c.Source, c.EvaluatedAt }),
            // Akış şeması için: her düğümün düz devamı ve karar dalları (koşul → hedef, döngü sınırı).
            Graph = m.PlayableNodes.Select(n => new
            {
                n.Id, n.Kind, n.Label, n.PhaseId, n.SubflowId, n.Next,
                Branches = n.Branches.Select(b => new { b.When, b.GoTo, b.MaxIterations })
            }),
            Complications = Array.Empty<object>()
        };
    }

    // Katalog öğesinin VR modeli (yoksa null); ayrıntı ve toplu liste: GET /api/assets/manifest.
    private object? AssetRef(string code)
        => _assets.For(code) is { } a ? new { a.AssetId, a.Url, a.Sha256, a.SizeBytes } : null;

    // Kursiyer yalnız kendi seansını görür; eğitmen hepsini. Başkasının seansı için "bulunamadı" (var olduğu sızmaz).
    private async Task<SessionRecord?> FindOwnedAsync(string sessionId)
    {
        var session = await _db.Sessions.FindAsync(sessionId);
        return session is not null && (session.UserId == User.UserId() || User.IsInstructor()) ? session : null;
    }

    private StepView? CurrentView(SessionRecord session)
        => IsFlow(session)
            ? _flow.View(session.CurrentNodeId, FlowProcedureService.Load(session))
            : ProcedureService.MapStep(_store.GetStep(session.CurrentStepId));

    // Seans listesi: eğitmen hepsini, kursiyer kendi seanslarını görür (en yeni önce, en fazla 200).
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] int take = 200)
    {
        var me = User.UserId();
        var query = _db.Sessions.AsQueryable();
        if (!User.IsInstructor()) query = query.Where(s => s.UserId == me);
        var rows = await query.OrderByDescending(s => s.StartedAt).Take(Math.Clamp(take, 1, 500))
            .GroupJoin(_db.Users, s => s.UserId, u => u.Id, (s, u) => new { s, u })
            .SelectMany(x => x.u.DefaultIfEmpty(), (x, u) => new
            {
                x.s.Id, x.s.UserId, Username = u != null ? u.Username ?? u.Name : x.s.UserId, x.s.ScenarioId,
                ProcedureModel = x.s.ProcedureModel ?? "v1", x.s.Status, x.s.Score, x.s.CorrectActions, x.s.WrongActions,
                x.s.SterileViolations, x.s.HintsUsed, x.s.ComplicationsTriggered, x.s.ComplicationsResolved,
                x.s.CurrentNodeId, x.s.CurrentStepId, x.s.StartedAt, x.s.CompletedAt
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<StartSessionResponse>> Start([FromBody] StartSessionRequest req)
    {
        var scenario = _store.GetScenario(req.ScenarioId);
        if (scenario is null)
            return NotFound(new { message = $"Senaryo bulunamadı: {req.ScenarioId}" });

        var first = _store.FirstStep();
        if (first is null)
            return StatusCode(500, new { message = "Prosedür adımı tanımlı değil." });

        // Seans, girişi yapan kullanıcıya aittir; istekteki userId yok sayılır (başkası adına seans açılamaz).
        var userId = User.UserId()!;

        var session = new SessionRecord
        {
            UserId = userId,
            ScenarioId = scenario.ScenarioId,
            ProcedureCode = scenario.ProcedureCode,
            CurrentStepId = first.StepId,
            Score = scenario.StartingScore,
            RawScore = scenario.StartingScore,
            Status = "IN_PROGRESS",
            ProcedureModel = "v1"
        };
        if (IsFlowModel(ActiveModel))
            _flow.Start(session, scenario, req.Seed);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new StartSessionResponse
        {
            SessionId = session.Id,
            ScenarioName = scenario.Name,
            ProcedureCode = scenario.ProcedureCode,
            CurrentStep = CurrentView(session),
            Score = session.Score,
            Status = session.Status,
            ProcedureModel = session.ProcedureModel ?? "v1"
        });
    }

    [HttpPost("{sessionId}/events")]
    public async Task<ActionResult<ValidationResponse>> PostEvent(string sessionId, [FromBody] EventRequest ev)
    {
        var session = await FindOwnedAsync(sessionId);
        if (session is null)
            return NotFound(new { message = "Seans bulunamadı." });
        if (session.Status != "IN_PROGRESS")
            return BadRequest(new { message = "Seans aktif değil." });

        // Idempotency: ignore a replayed event id and return current state.
        if (!string.IsNullOrEmpty(ev.EventId) &&
            await _db.SessionEvents.AnyAsync(e => e.SessionId == sessionId && e.EventId == ev.EventId))
        {
            return Ok(new ValidationResponse
            {
                Success = true,
                CurrentStepId = session.CurrentStepId ?? 0,
                NextStepId = session.CurrentStepId,
                CurrentNodeId = session.CurrentNodeId,
                NextNodeId = session.CurrentNodeId,
                Score = session.Score,
                Message = "Bu olay daha önce işlendi (yinelenen).",
                NextStep = CurrentView(session)
            });
        }

        _logger.LogInformation("VR→REST | session={SessionId} event={Type} instrument={Instrument} target={Target} suture={Suture}",
            sessionId, ev.EventType, ev.InstrumentCode ?? "-", ev.Target ?? "-", ev.SutureCode ?? "-");

        // Sözleşmeye uymayan olay entegrasyon hatasıdır: kayda ve puana girmez (docs/vr-events.md).
        if (IsFlow(session) && _flow.ContractError(ev) is { } contract)
            return BadRequest(new { message = contract.Message, field = contract.Field, allowed = contract.Allowed });

        var scoreBefore = session.Score;
        var outcome = IsFlow(session) ? _flow.Validate(session, ev) : _procedure.Validate(session, ev);

        _db.SessionEvents.Add(new SessionEventRecord
        {
            SessionId = sessionId,
            EventId = ev.EventId,
            EventType = ev.EventType,
            StepId = outcome.CurrentStepId,
            NodeId = outcome.CurrentNodeId,
            InstrumentCode = ev.InstrumentCode,
            IsSuccess = outcome.Success,
            DeviationType = outcome.Deviation?.DeviationType,
            ScoreChange = outcome.ScoreChange,
            PayloadJson = JsonSerializer.Serialize(ev)
        });
        var sessionEventId = _db.ChangeTracker.Entries<SessionEventRecord>().First().Entity.Id;

        if (outcome.Completed)
        {
            session.Status = "COMPLETED";
            session.CompletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        _logger.LogInformation("REST→VR | session={SessionId} step={Step} success={Success} deviation={Deviation} scoreChange={Change} score={Score}",
            sessionId, outcome.CurrentStepId, outcome.Success, outcome.Deviation?.DeviationType ?? "-", outcome.ScoreChange, session.Score);

        // Puan değişmediyse (doğru adım, yinelenen olay) ScoreUpdate gönderilmez; puan zaten olay cevabında var.
        if (session.Score != scoreBefore)
            await _hub.Clients.Group(sessionId).SendAsync("ScoreUpdate",
                new ScoreUpdateDto { SessionId = sessionId, Score = session.Score });

        AiFeedbackDto? complication = null;
        CumulativeComplicationDto? cumulative = null;
        if (outcome.Deviation is not null)
        {
            var table = TableEntry(session, outcome.CurrentStepId, outcome.CurrentNodeId, outcome.Deviation);
            complication = BuildComplication(sessionId, ev.EventId, outcome.CurrentStepId, outcome.Deviation, table);

            var deviations = await _db.SessionEvents
                .Where(e => e.SessionId == sessionId && e.DeviationType != null)
                .OrderBy(e => e.CreatedAt)
                .Select(e => new DeviationSample(e.StepId, e.DeviationType!))
                .ToListAsync();
            cumulative = IsFlow(session)
                ? _complications.Build(deviations, session.ConsecutiveErrors, (step, type) => _flowText.Entry(step.ToString(), type, null))
                : _complications.Build(deviations, session.ConsecutiveErrors);

            var patient = _store.GetScenario(session.ScenarioId)?.Patient ?? new PatientProfile();
            // Ameliyat öncesi (ana adım 0): AI'ın kaynak parçası yok; modelin risk metni yeterli, çağrı yapılmaz.
            if (outcome.CurrentStepId > 0)
                QueueAiFeedback(sessionId, sessionEventId, ev.EventId, outcome.CurrentStepId, outcome.Deviation, patient,
                outcome.ActiveComplication, session.ProcedureModel ?? "v1", table);
        }

        return Ok(new ValidationResponse
        {
            Success = outcome.Success,
            CurrentStepId = outcome.CurrentStepId,
            NextStepId = outcome.NextStepId,
            Score = session.Score,
            Deviation = outcome.Deviation,
            Message = outcome.Message,
            AllowRetry = outcome.AllowRetry,
            Completed = outcome.Completed,
            NextStep = outcome.NextStep,
            Complication = complication,
            CumulativeComplication = cumulative,
            ActiveComplication = outcome.ActiveComplication,
            CurrentNodeId = outcome.CurrentNodeId,
            NextNodeId = outcome.NextNodeId,
            Branch = outcome.Branch,
            Adaptive = outcome.Adaptive
        });
    }

    [HttpPost("{sessionId}/complete")]
    public async Task<ActionResult<ReportResponse>> Complete(string sessionId)
    {
        var session = await FindOwnedAsync(sessionId);
        if (session is null)
            return NotFound(new { message = "Seans bulunamadı." });

        if (session.Status == "IN_PROGRESS")
        {
            session.Status = "COMPLETED";
            session.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var totalActions = session.CorrectActions + session.WrongActions;
        var successRate = totalActions == 0
            ? 0
            : (int)Math.Round(100.0 * session.CorrectActions / totalActions);

        return Ok(new ReportResponse
        {
            Score = session.Score,
            SuccessRate = successRate,
            CorrectActions = session.CorrectActions,
            WrongActions = session.WrongActions,
            SterileViolations = session.SterileViolations,
            HintsUsed = session.HintsUsed,
            ComplicationsTriggered = session.ComplicationsTriggered,
            ComplicationsResolved = session.ComplicationsResolved,
            Summary = BuildSummary(session, successRate) + AdaptiveSummary(session)
        });
    }

    [HttpGet("{sessionId}")]
    public async Task<ActionResult<StartSessionResponse>> Get(string sessionId)
    {
        var session = await FindOwnedAsync(sessionId);
        if (session is null)
            return NotFound(new { message = "Seans bulunamadı." });

        var scenario = _store.GetScenario(session.ScenarioId);
        return Ok(new StartSessionResponse
        {
            SessionId = session.Id,
            ScenarioName = scenario?.Name ?? session.ScenarioId,
            ProcedureCode = session.ProcedureCode,
            CurrentStep = CurrentView(session),
            Score = session.Score,
            Status = session.Status,
            ActiveComplication = IsFlow(session) ? null : _procedure.MapActive(session),
            ProcedureModel = session.ProcedureModel ?? "v1"
        });
    }

    // 60 adımlık seansın yürüdüğü yol ve kararların girdileri: akış şemasında doğruluğu gözle denetlemek için.
    [HttpGet("{sessionId}/flow")]
    public async Task<ActionResult> GetFlow(string sessionId)
    {
        var session = await FindOwnedAsync(sessionId);
        if (session is null)
            return NotFound(new { message = "Seans bulunamadı." });
        if (!IsFlow(session))
            return BadRequest(new { message = "Bu seans eski 25 adımlı modelle çalışıyor; akış şeması yalnızca 60 adımlık modelde var." });

        var state = FlowProcedureService.Load(session);
        var conditions = _flowModel.Graph.Conditions;
        return Ok(new
        {
            session.ScenarioId,
            Path = state.Path,
            session.CurrentNodeId,
            state.OpenBranch,
            // Koşul planı: her koşulun ardışık değerlendirmelerde alacağı değer ve şimdiye kadar kaç kez değerlendirildiği.
            Conditions = conditions.Values.Where(c => c.Source != "OUTCOME").Select(c => new
            {
                c.Code, c.Description, c.Source, c.EvaluatedAt,
                Plan = state.Conditions.GetValueOrDefault(c.Code),
                Evaluations = state.Evaluations.Where(e => e.Key.EndsWith("|" + c.Code)).Sum(e => e.Value),
                Taken = state.Taken.Where(e => e.Key.EndsWith("|" + c.Code)).Sum(e => e.Value)
            }),
            Parameters = state.Parameters,
            Counts = new { Recorded = state.RecordedCounts, Table = state.Counts },
            Requests = state.Requests.ToDictionary(r => r.Key, r => r.Value.Select(_flowModel.InstrumentName).ToList()),
            state.AdaptiveMode,
            state.FixedConditions,
            state.AdaptiveDecisions
        });
    }

    [HttpGet("{sessionId}/events")]
    public async Task<ActionResult> GetEvents(string sessionId)
    {
        if (await FindOwnedAsync(sessionId) is null)
            return NotFound(new { message = "Seans bulunamadı." });
        var events = await _db.SessionEvents
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new
            {
                e.EventId, e.EventType, e.StepId, e.NodeId, e.InstrumentCode,
                e.IsSuccess, e.DeviationType, e.ScoreChange, e.CreatedAt, e.PayloadJson
            })
            .ToListAsync();
        return Ok(events);
    }

    [HttpGet("{sessionId}/feedbacks")]
    public async Task<ActionResult> GetFeedbacks(string sessionId)
    {
        if (await FindOwnedAsync(sessionId) is null)
            return NotFound(new { message = "Seans bulunamadı." });
        var feedbacks = await _db.AiFeedbacks
            .Where(f => f.SessionId == sessionId)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new
            {
                f.StepId, f.DeviationType, f.PossibleRisk, f.Explanation,
                f.RecommendedAction, f.Severity, f.Source, f.ModelName,
                f.PromptTokens, f.CompletionTokens, f.TotalTokens, f.CreatedAt
            })
            .ToListAsync();
        return Ok(feedbacks);
    }

    // Sapmanın deterministik klinik metni: eski modelde klinik tablo, 60 adımlık modelde adımın onaylı hata senaryoları.
    private ClinicalKnowledgeEntry? TableEntry(SessionRecord session, int stepId, string? nodeId, DeviationDto dev)
    {
        if (!IsFlow(session))
            return _store.GetClinicalEntry(stepId, dev.DeviationType);
        if (dev.DeviationType == "CHECKLIST_INCOMPLETE")
            return _flowText.Entry(nodeId ?? "", dev.DeviationType, dev.Actual, (dev.Expected ?? "").Split(" / "));
        var wanted = (dev.Expected ?? "").Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(_flowModel.Catalog.ContainsKey).ToList();
        return _flowText.Entry(nodeId ?? stepId.ToString(), dev.DeviationType, dev.Actual, wanted);
    }

    private static AiFeedbackDto BuildComplication(string sessionId, string eventId, int stepId, DeviationDto dev, ClinicalKnowledgeEntry? e)
    {
        return new AiFeedbackDto
        {
            SessionId = sessionId,
            EventId = eventId,
            StepId = stepId,
            DeviationType = dev.DeviationType,
            PossibleRisk = e?.PossibleRisk ?? "Prosedür kuralına aykırı bir işlem tespit edildi.",
            Explanation = e?.Explanation ?? "Bu eylem kanonik prosedür sırasına veya alet uygunluğuna aykırıdır.",
            RecommendedAction = e?.RecommendedAction ?? "Doğru adımı ve uygun aleti kontrol ederek işlemi tekrarlayın.",
            Severity = dev.Severity,
            Source = "table"
        };
    }

    // Hatanın yol açtığı durumlar (60 adımlık model): debrief için rapora eklenir.
    private string AdaptiveSummary(SessionRecord s)
    {
        if (!IsFlow(s)) return "";
        var decisions = FlowProcedureService.Load(s).AdaptiveDecisions;
        if (decisions.Count == 0) return "";
        var parts = decisions.Select(d =>
            $"{_flowModel.Graph.Conditions.GetValueOrDefault(d.Condition)?.Description ?? d.Condition} (Adım {d.AtNode}{(d.ScenarioId is null ? "" : ", " + d.ScenarioId)})");
        return $" Hatalarınızın sonucu: {string.Join("; ", parts)}.";
    }

    private static string BuildSummary(SessionRecord s, int successRate)
    {
        var totalActions = s.CorrectActions + s.WrongActions;
        var verdict = (s.WrongActions == 0 && successRate >= 95)
            ? "Genel performans başarılı."
            : successRate >= 75
                ? "Performans yeterli; hatalar tekrar edilmemeli."
                : successRate >= 50
                    ? "Performans sınırda; prosedür sırası ve alet seçimi geliştirilmeli."
                    : "Performans yetersiz; çok sayıda hata var, prosedür baştan çalışılmalı.";
        var comp = s.ComplicationsTriggered == 0
            ? "Komplikasyon gelişmedi."
            : IsFlow(s)
                ? $"Komplikasyon/ek durum: {s.ComplicationsTriggered} gelişti, {s.ComplicationsResolved} tanesi akış içinde yönetilip ana akışa dönüldü."
                : $"Komplikasyon: {s.ComplicationsTriggered} gelişti, {s.ComplicationsResolved} tanesi doğru malzeme zinciriyle giderildi.";
        return $"Toplam {totalActions} aksiyondan {s.CorrectActions} doğru, {s.WrongActions} hatalı (başarı %{successRate}). " +
               $"Steril alan ihlali: {s.SterileViolations}, kullanılan ipucu: {s.HintsUsed}. {comp} Skor: {s.Score}/100. {verdict}";
    }

    private void QueueAiFeedback(string sessionId, string sessionEventId, string eventId,
        int stepId, DeviationDto deviation, PatientProfile patient, ActiveComplicationDto? complication,
        string procedureModel, ClinicalKnowledgeEntry? table)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ai = scope.ServiceProvider.GetRequiredService<IAiFeedbackService>();
                var db = scope.ServiceProvider.GetRequiredService<ArqenionDbContext>();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<SimulationHub>>();

                var result = await ai.GenerateAsync(
                    new AiFeedbackRequest(sessionId, eventId, stepId, deviation, patient, complication, procedureModel, table),
                    CancellationToken.None);
                if (result is null) return;

                db.AiFeedbacks.Add(new AiFeedbackRecord
                {
                    SessionId = sessionId,
                    SessionEventId = sessionEventId,
                    StepId = stepId,
                    DeviationType = result.DeviationType,
                    PossibleRisk = result.PossibleRisk,
                    Explanation = result.Explanation,
                    RecommendedAction = result.RecommendedAction,
                    Severity = result.Severity,
                    Source = result.Source,
                    ModelName = result.ModelName,
                    PromptTokens = result.PromptTokens,
                    CompletionTokens = result.CompletionTokens,
                    TotalTokens = result.TotalTokens,
                    RequestJson = result.RequestJson,
                    ResponseJson = result.ResponseJson
                });
                await db.SaveChangesAsync();

                // Deterministik komplikasyon zaten REST yanıtında döndü; SignalR'ı yalnızca LLM zenginleştirmesi için kullan.
                if (result.Source != "llm") return;

                await hub.Clients.Group(sessionId).SendAsync("AiFeedback", new AiFeedbackDto
                {
                    SessionId = sessionId,
                    EventId = eventId,
                    StepId = stepId,
                    DeviationType = result.DeviationType,
                    PossibleRisk = result.PossibleRisk,
                    Explanation = result.Explanation,
                    RecommendedAction = result.RecommendedAction,
                    Severity = result.Severity,
                    Source = result.Source,
                    ModelName = result.ModelName,
                    PromptTokens = result.PromptTokens,
                    CompletionTokens = result.CompletionTokens,
                    TotalTokens = result.TotalTokens
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI feedback background task failed for session {SessionId}.", sessionId);
            }
        });
    }
}
