using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

public sealed class ValidationOutcome
{
    public bool Success { get; set; }
    public int CurrentStepId { get; set; }
    public int? NextStepId { get; set; }
    public DeviationDto? Deviation { get; set; }
    public string Message { get; set; } = "";
    public bool AllowRetry { get; set; }
    public bool Completed { get; set; }
    public int ScoreChange { get; set; }
    public StepView? NextStep { get; set; }
    // Malzeme seçimiyle tetiklenen komplikasyonun anlık durumu (yeni açıldı, ilerledi veya çözüldü).
    public ActiveComplicationDto? ActiveComplication { get; set; }
    public string? CurrentNodeId { get; set; }
    public string? NextNodeId { get; set; }
    public FlowBranchDto? Branch { get; set; }
}

// Deterministic, fully explainable procedure validation. This is the safety-critical layer:
// it decides whether an action is correct; the LLM only explains deviations it flags.
public sealed class ProcedureService
{
    private readonly ProcedureStore _store;
    private readonly ScoringService _scoring;

    public ProcedureService(ProcedureStore store, ScoringService scoring)
    {
        _store = store;
        _scoring = scoring;
    }

    public ValidationOutcome Validate(SessionRecord session, EventRequest ev)
    {
        var step = _store.GetStep(session.CurrentStepId);
        if (step is null)
        {
            return new ValidationOutcome
            {
                Success = false,
                CurrentStepId = session.CurrentStepId ?? 0,
                Message = "Seans tamamlanmış veya geçerli bir adım bulunamadı.",
                Completed = true
            };
        }

        // Açık bir komplikasyon varsa olaylar önce çözüm zincirine yönlenir.
        var active = _store.GetComplication(session.ActiveComplicationId);
        if (active is not null && ev.EventType != "hint_requested")
            return ResolveComplication(session, step, active, ev);

        // Hint request: neutral guidance with a small penalty, stays on the same step.
        if (ev.EventType == "hint_requested")
        {
            session.HintsUsed++;
            _scoring.Apply(session, -2);
            var expected = active is not null
                ? ExpectedText(active.Resolution[session.ComplicationStepIndex])
                : step.RequiredSuture ?? string.Join(" / ", step.AllowedInstruments);
            var title = active is not null
                ? $"{active.Title} — {active.Resolution[session.ComplicationStepIndex].Title}"
                : step.Title;
            return new ValidationOutcome
            {
                Success = false,
                CurrentStepId = step.StepId,
                NextStepId = step.StepId,
                Message = $"İpucu: {title}. Beklenen: {expected}",
                AllowRetry = true,
                ScoreChange = -2,
                ActiveComplication = active is null ? null : BuildActive(active, session.ComplicationStepIndex, false)
            };
        }

        // Always-deviations, independent of the expected event.
        if (ev.EventType == "sterile_area_violated")
        {
            session.SterileViolations++;
            return Deviate(session, step, "STERILE_VIOLATION", null, null, "Steril alan ihlali algılandı.");
        }
        if (ev.EventType == "instrument_dropped")
        {
            return Deviate(session, step, "INSTRUMENT_DROPPED", null, ev.InstrumentCode, "Alet düşürüldü; sterilite kaybı.");
        }

        // Wrong action type for the current step.
        if (ev.EventType != step.ExpectedEvent)
        {
            return Deviate(session, step, "WRONG_ORDER", step.ExpectedEvent, ev.EventType,
                $"Bu adımda beklenen işlem: {step.ExpectedEvent}.");
        }

        // Event type matches expectation; validate the details.
        switch (step.ExpectedEvent)
        {
            case "count_confirmed":
                if (step.ExpectedCounts is { Count: > 0 } expectedCounts && !CountsMatch(expectedCounts, ev.Counts))
                {
                    return Deviate(session, step, "COUNT_MISMATCH", Format(expectedCounts), Format(ev.Counts),
                        "Sayım uyuşmazlığı; kapanış öncesi tekrar sayım yapılmalıdır.");
                }
                break;

            case "instrument_delivered":
            case "instrument_placed":
                var instrumentOk = !string.IsNullOrEmpty(ev.InstrumentCode)
                                   && step.AllowedInstruments.Contains(ev.InstrumentCode);
                var sutureOk = step.RequiredSuture is null || ev.SutureCode == step.RequiredSuture;

                // Yanlış seçim bir senaryo komplikasyonu tetikliyorsa, o komplikasyon genel hata mesajının önüne geçer.
                if (!instrumentOk || !sutureOk)
                {
                    var triggered = _store.MatchComplication(step.StepId,
                        instrumentOk ? null : ev.InstrumentCode,
                        sutureOk ? null : ev.SutureCode);
                    if (triggered is not null)
                        return TriggerComplication(session, step, triggered, ev);
                }

                // Cihazların yalnızca steril uçları sahaya verilir; cihaz gövdesi steril alana giremez.
                // Bu kontrol "yanlış alet"ten önce gelir: hata alet seçimi değil, steril bariyer ihlalidir.
                if (step.Target == "SURGEON" && !string.IsNullOrEmpty(ev.InstrumentCode)
                    && _store.GetInstrument(ev.InstrumentCode) is { Sterile: false } nonSterile)
                {
                    session.SterileViolations++;
                    var hint = nonSterile.Category == "Cihaz"
                        ? " Cihazın yalnızca steril ucu sahaya verilir."
                        : "";
                    return Deviate(session, step, "STERILE_VIOLATION",
                        $"steril malzeme ({string.Join(" / ", step.AllowedInstruments)})", nonSterile.Name,
                        $"{nonSterile.Name} steril değildir; steril sahaya verilemez.{hint}");
                }
                if (!instrumentOk)
                {
                    return Deviate(session, step, "WRONG_INSTRUMENT",
                        string.Join(" / ", step.AllowedInstruments), ev.InstrumentCode,
                        "Bu aşamada uygun olmayan bir alet kullanıldı.");
                }
                if (!sutureOk)
                {
                    return Deviate(session, step, "WRONG_INSTRUMENT", step.RequiredSuture, ev.SutureCode,
                        "Bu aşamada yanlış sütür seçildi.");
                }
                if (step.Target is not null && !string.IsNullOrEmpty(ev.Target) && ev.Target != step.Target)
                {
                    var type = step.Penalties.ContainsKey("CONTAMINATED_TOOL_REUSE")
                        ? "CONTAMINATED_TOOL_REUSE" : "WRONG_ORDER";
                    return Deviate(session, step, type, step.Target, ev.Target,
                        "Alet yanlış alana yönlendirildi.");
                }
                break;
        }

        // Correct action.
        _scoring.Apply(session, step.SuccessScore);
        session.CorrectActions++;
        session.ConsecutiveErrors = 0;
        session.CurrentStepId = step.NextStepId;
        var completed = step.NextStepId is null;

        return new ValidationOutcome
        {
            Success = true,
            CurrentStepId = step.StepId,
            NextStepId = step.NextStepId,
            Message = completed ? "Prosedür tamamlandı." : "Doğru işlem.",
            AllowRetry = false,
            Completed = completed,
            ScoreChange = step.SuccessScore,
            NextStep = MapStep(_store.GetStep(step.NextStepId))
        };
    }

    // Yanlış malzeme seçimi komplikasyonu açar: ceza uygulanır, adım tekrar edilmek üzere açık kalır.
    private ValidationOutcome TriggerComplication(SessionRecord session, ProcedureStep step,
        ComplicationDefinition c, EventRequest ev)
    {
        session.WrongActions++;
        session.ConsecutiveErrors++;
        session.ComplicationsTriggered++;
        _scoring.Apply(session, -c.Penalty);

        var actual = ev.SutureCode is not null && c.TriggerSutures.Contains(ev.SutureCode)
            ? ev.SutureCode
            : ev.InstrumentCode;

        // Teknikerin gideremeyeceği komplikasyon yalnızca kayda geçer; adım doğru malzemeyle tekrarlanır.
        if (!c.ScrubResolvable || c.Resolution.Count == 0)
        {
            return new ValidationOutcome
            {
                Success = false,
                CurrentStepId = step.StepId,
                NextStepId = step.StepId,
                Deviation = new DeviationDto
                {
                    DeviationType = "CONTAMINATED_TOOL_REUSE",
                    Expected = step.RequiredSuture ?? string.Join(" / ", step.AllowedInstruments),
                    Actual = actual,
                    Severity = c.Severity
                },
                Message = $"KOMPLİKASYON — {c.Title}. {c.ResolutionNote}",
                AllowRetry = true,
                ScoreChange = -c.Penalty,
                ActiveComplication = BuildActive(c, 0, resolved: true)
            };
        }

        session.ActiveComplicationId = c.ComplicationId;
        session.ComplicationStepIndex = 0;

        return new ValidationOutcome
        {
            Success = false,
            CurrentStepId = step.StepId,
            NextStepId = step.StepId,
            Deviation = new DeviationDto
            {
                DeviationType = "WRONG_INSTRUMENT",
                Expected = step.RequiredSuture ?? string.Join(" / ", step.AllowedInstruments),
                Actual = actual,
                Severity = c.Severity
            },
            Message = $"KOMPLİKASYON — {c.Title}. Çözüm için gerekli malzemeleri sırayla teslim edin.",
            AllowRetry = true,
            ScoreChange = -c.Penalty,
            ActiveComplication = BuildActive(c, 0, resolved: false)
        };
    }

    // Komplikasyon açıkken gelen olaylar çözüm zincirinin sıradaki malzemesine karşı doğrulanır.
    private ValidationOutcome ResolveComplication(SessionRecord session, ProcedureStep step,
        ComplicationDefinition c, EventRequest ev)
    {
        var index = Math.Clamp(session.ComplicationStepIndex, 0, c.Resolution.Count - 1);
        var action = c.Resolution[index];

        if (ev.EventType == "sterile_area_violated")
        {
            session.SterileViolations++;
            var outcome = Deviate(session, step, "STERILE_VIOLATION", null, null,
                "Komplikasyon yönetimi sırasında steril alan ihlali.");
            outcome.ActiveComplication = BuildActive(c, index, false);
            return outcome;
        }

        var matches = ev.EventType == action.ExpectedEvent
            && !string.IsNullOrEmpty(ev.InstrumentCode)
            && action.AllowedInstruments.Contains(ev.InstrumentCode)
            && (action.RequiredSuture is null || ev.SutureCode == action.RequiredSuture)
            && (action.Target is null || string.IsNullOrEmpty(ev.Target) || ev.Target == action.Target);

        if (!matches)
        {
            session.WrongActions++;
            session.ConsecutiveErrors++;
            _scoring.Apply(session, -c.MisstepPenalty);
            return new ValidationOutcome
            {
                Success = false,
                CurrentStepId = step.StepId,
                NextStepId = step.StepId,
                Deviation = new DeviationDto
                {
                    DeviationType = "COMPLICATION_MISMANAGED",
                    Expected = ExpectedText(action),
                    Actual = ev.SutureCode is not null ? $"{ev.InstrumentCode} + {ev.SutureCode}" : ev.InstrumentCode,
                    Severity = c.Severity
                },
                Message = $"Komplikasyon yönetimi hatalı. Beklenen: {action.Title}",
                AllowRetry = true,
                ScoreChange = -c.MisstepPenalty,
                ActiveComplication = BuildActive(c, index, false)
            };
        }

        // Doğru malzeme: zincirde bir adım ilerle.
        session.CorrectActions++;
        session.ConsecutiveErrors = 0;
        var next = index + 1;

        if (next < c.Resolution.Count)
        {
            session.ComplicationStepIndex = next;
            return new ValidationOutcome
            {
                Success = true,
                CurrentStepId = step.StepId,
                NextStepId = step.StepId,
                Message = $"Komplikasyon yönetimi — {action.Title} tamam ({next}/{c.Resolution.Count}).",
                AllowRetry = true,
                ScoreChange = 0,
                ActiveComplication = BuildActive(c, next, false)
            };
        }

        // Zincir tamamlandı: komplikasyon kapanır, asıl adım doğru malzemeyle tekrar edilir.
        session.ActiveComplicationId = null;
        session.ComplicationStepIndex = 0;
        session.ComplicationsResolved++;
        _scoring.Apply(session, c.ResolutionScore);

        return new ValidationOutcome
        {
            Success = true,
            CurrentStepId = step.StepId,
            NextStepId = step.StepId,
            Message = $"Komplikasyon giderildi — {c.Title}. Adıma doğru malzeme ile devam edin: {step.Title}",
            AllowRetry = true,
            ScoreChange = c.ResolutionScore,
            NextStep = MapStep(step),
            ActiveComplication = BuildActive(c, c.Resolution.Count, resolved: true)
        };
    }

    // Seansın kaldığı yerden devam edebilmesi için açık komplikasyonun durumunu döner.
    public ActiveComplicationDto? MapActive(SessionRecord session)
    {
        var c = _store.GetComplication(session.ActiveComplicationId);
        return c is null ? null : BuildActive(c, session.ComplicationStepIndex, false);
    }

    private static ActiveComplicationDto BuildActive(ComplicationDefinition c, int index, bool resolved)
    {
        var actions = c.Resolution.Select((r, i) => new ResolutionStepView
        {
            Order = i + 1,
            Title = r.Title,
            ExpectedEvent = r.ExpectedEvent,
            Target = r.Target,
            AllowedInstruments = r.AllowedInstruments,
            RequiredSuture = r.RequiredSuture,
            Note = r.Note,
            Done = resolved || i < index
        }).ToList();

        return new ActiveComplicationDto
        {
            ComplicationId = c.ComplicationId,
            Title = c.Title,
            Narrative = c.Narrative,
            ResolutionNote = c.ResolutionNote,
            Severity = c.Severity,
            StepId = c.StepId,
            ScrubResolvable = c.ScrubResolvable,
            ResolutionIndex = resolved ? c.Resolution.Count : index,
            ResolutionTotal = c.Resolution.Count,
            Resolved = resolved,
            CurrentAction = resolved || index >= actions.Count ? null : actions[index],
            Actions = actions
        };
    }

    private static string ExpectedText(ResolutionStep r)
        => r.RequiredSuture is null
            ? string.Join(" / ", r.AllowedInstruments)
            : $"{string.Join(" / ", r.AllowedInstruments)} + {r.RequiredSuture}";

    private ValidationOutcome Deviate(SessionRecord session, ProcedureStep step, string type,
        string? expected, string? actual, string message)
    {
        session.WrongActions++;
        session.ConsecutiveErrors++;
        var penalty = step.Penalties.TryGetValue(type, out var p) ? p : DefaultPenalty(type);
        // Ardışık hatalarda ceza kademeli olarak ağırlaşır.
        var escalatedPenalty = penalty + (session.ConsecutiveErrors - 1) * 3;
        _scoring.Apply(session, -escalatedPenalty);
        var baseSeverity = _store.GetClinicalEntry(step.StepId, type)?.Severity ?? "MEDIUM";
        var severity = EscalateSeverity(baseSeverity, session.ConsecutiveErrors);

        return new ValidationOutcome
        {
            Success = false,
            CurrentStepId = step.StepId,
            NextStepId = step.StepId,
            Deviation = new DeviationDto { DeviationType = type, Expected = expected, Actual = actual, Severity = severity },
            Message = message,
            AllowRetry = true,
            ScoreChange = -escalatedPenalty
        };
    }

    // Ardışık hata sayısına göre klinik şiddeti bir üst kademeye taşır.
    private static string EscalateSeverity(string baseSeverity, int streak)
    {
        var rank = baseSeverity switch { "LOW" => 1, "MEDIUM" => 2, "HIGH" => 3, "CRITICAL" => 4, _ => 2 };
        rank += Math.Max(0, streak - 1);
        return rank switch { <= 1 => "LOW", 2 => "MEDIUM", 3 => "HIGH", _ => "CRITICAL" };
    }

    private static bool CountsMatch(Dictionary<string, int> expected, Dictionary<string, int>? actual)
    {
        if (actual is null) return false;
        return expected.All(kv => actual.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    private static string Format(Dictionary<string, int>? counts)
        => counts is null ? "-" : string.Join(", ", counts.Select(kv => $"{kv.Key}:{kv.Value}"));

    private static int DefaultPenalty(string type) => type switch
    {
        "STERILE_VIOLATION" => 15,
        "COUNT_MISMATCH" => 20,
        "INSTRUMENT_DROPPED" => 10,
        "CONTAMINATED_TOOL_REUSE" => 12,
        "COMPLICATION_MISMANAGED" => 8,
        "WRONG_INSTRUMENT" => 8,
        "WRONG_ORDER" => 5,
        _ => 5
    };

    public static StepView? MapStep(ProcedureStep? step) => step is null ? null : new StepView
    {
        StepId = step.StepId,
        PhaseNo = step.PhaseNo,
        Title = step.Title,
        Phase = step.Phase,
        ExpectedEvent = step.ExpectedEvent,
        AllowedInstruments = step.AllowedInstruments,
        RequiredSuture = step.RequiredSuture,
        Target = step.Target
    };
}
