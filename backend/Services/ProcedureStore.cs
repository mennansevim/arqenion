using System.Text.Json;
using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// Loads and caches the procedure definition, scenarios and clinical knowledge table from disk.
public sealed class ProcedureStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ProcedureDefinition _procedure;
    private readonly Dictionary<string, Scenario> _scenarios;
    private readonly List<ClinicalKnowledgeEntry> _clinical;

    public ProcedureStore(IHostEnvironment env, ILogger<ProcedureStore> logger)
    {
        var dir = Path.Combine(env.ContentRootPath, "Procedures");

        _procedure = Load<ProcedureDefinition>(Path.Combine(dir, "open-appendectomy-v1.json"))
                     ?? throw new InvalidOperationException("Procedure definition could not be loaded.");

        var scenariosFile = Load<ScenariosFile>(Path.Combine(dir, "scenarios.json")) ?? new ScenariosFile();
        _scenarios = scenariosFile.Scenarios.ToDictionary(s => s.ScenarioId, StringComparer.OrdinalIgnoreCase);

        var clinicalFile = Load<ClinicalKnowledgeFile>(Path.Combine(dir, "clinical-knowledge.json")) ?? new ClinicalKnowledgeFile();
        _clinical = clinicalFile.Entries;

        logger.LogInformation("Loaded procedure '{Code}' with {Steps} steps, {Scenarios} scenarios, {Entries} clinical entries.",
            _procedure.ProcedureCode, _procedure.Steps.Count, _scenarios.Count, _clinical.Count);
    }

    public ProcedureDefinition Procedure => _procedure;

    public Scenario? GetScenario(string scenarioId)
        => _scenarios.TryGetValue(scenarioId, out var s) ? s : null;

    public ProcedureStep? GetStep(int? stepId)
        => stepId is null ? null : _procedure.Steps.FirstOrDefault(s => s.StepId == stepId);

    public ProcedureStep? FirstStep()
        => _procedure.Steps.OrderBy(s => s.StepId).FirstOrDefault();

    public ComplicationDefinition? GetComplication(string? complicationId)
        => complicationId is null
            ? null
            : _procedure.Complications.FirstOrDefault(c =>
                string.Equals(c.ComplicationId, complicationId, StringComparison.OrdinalIgnoreCase));

    // Bu adımda teslim edilen alet/sütür bir komplikasyon tetikliyor mu?
    public ComplicationDefinition? MatchComplication(int stepId, string? instrumentCode, string? sutureCode)
        => _procedure.Complications.FirstOrDefault(c => c.StepId == stepId &&
            ((instrumentCode is not null && c.TriggerInstruments.Contains(instrumentCode)) ||
             (sutureCode is not null && c.TriggerSutures.Contains(sutureCode))));

    public InstrumentItem? GetInstrument(string? code)
        => code is null ? null : _procedure.InstrumentCatalog.FirstOrDefault(i => i.Code == code);

    // Exact (stepId, deviation) match first, then wildcard (*, deviation).
    public ClinicalKnowledgeEntry? GetClinicalEntry(int stepId, string deviationType)
        => _clinical.FirstOrDefault(e => e.StepId == stepId.ToString() && e.DeviationType == deviationType)
           ?? _clinical.FirstOrDefault(e => e.StepId == "*" && e.DeviationType == deviationType);

    private static T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }
}
