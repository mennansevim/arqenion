using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// Applies score changes within the procedure's bounds. Deterministic; the LLM never touches the score.
public sealed class ScoringService
{
    private readonly ProcedureStore _store;

    public ScoringService(ProcedureStore store) => _store = store;

    public void Apply(SessionRecord session, int change, ScoreBounds? bounds = null)
    {
        bounds ??= _store.Procedure.ScoreBounds;
        // RawScore da sınırlar içinde tutulur; aksi halde çok negatif birikince skor 0'da kilitlenir ve toparlanamaz.
        session.RawScore = Math.Clamp(session.RawScore + change, bounds.Min, bounds.Max);
        session.Score = session.RawScore;
    }
}
