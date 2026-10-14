using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// Deterministik yol bulma: bir düğüm doğru tamamlandığında sıradaki düğümü seçer.
// procedure-model/tools/flow_engine.py walk() ile aynı kurallar; golden testler eşitliği denetler.
public sealed class FlowEngine
{
    private const int DefaultMaxLoop = 1;   // walk(max_loop=1)
    private readonly FlowModelStore _model;

    public FlowEngine(FlowModelStore model) => _model = model;

    public FlowState Start(Dictionary<string, List<bool>> conditions, Dictionary<string, System.Text.Json.JsonElement> parameters)
    {
        foreach (var c in conditions.Keys)
            if (!_model.Graph.Conditions.ContainsKey(c))
                throw new ArgumentException($"Bilinmeyen koşul: {c}");
        var p = _model.DefaultParameters();
        foreach (var (k, v) in parameters) p[k] = v;
        return new FlowState { Conditions = conditions, Parameters = p, Path = new() { _model.FirstNodeId } };
    }

    // Düğümün karar noktalarını sırayla değerlendirir; ilk doğru koşulun dalına gider, yoksa düz devam eder.
    // Dönüş: (sıradaki düğüm ya da null = prosedür bitti, alınan dalın koşulu ya da null).
    public (string? Next, string? Condition) Advance(FlowState state, string nodeId)
    {
        var n = _model.Node(nodeId) ?? throw new ArgumentException($"Bilinmeyen düğüm: {nodeId}");
        string? next = n.Next, taken = null;
        foreach (var b in n.Branches)
        {
            var key = $"{nodeId}|{b.When}";
            var limit = b.MaxIterations ?? DefaultMaxLoop;
            if (state.Taken.GetValueOrDefault(key) >= limit)
                continue;
            var i = state.Evaluations.GetValueOrDefault(key);
            state.Evaluations[key] = i + 1;
            if (Value(state.Conditions.GetValueOrDefault(b.When), i))
            {
                state.Taken[key] = state.Taken.GetValueOrDefault(key) + 1;
                next = b.GoTo;
                taken = b.When;
                break;
            }
        }
        if (next is not null) state.Path.Add(next);
        state.Delivered.Clear();
        return (next, taken);
    }

    // Python: seq[i] if i < len(seq) else (seq[-1] if len(seq) == 1 else False); plan yoksa False.
    private static bool Value(List<bool>? seq, int i)
        => seq is { Count: > 0 } && (i < seq.Count ? seq[i] : seq.Count == 1 && seq[0]);
}
