using System.Collections.Concurrent;

namespace Arqenion.Api.Services;

// AI çağrılarının maliyet ve hız sınırı. Sınır aşılınca çağrı yapılmaz; kursiyer yine anında klinik kartı görür.
//   - aynı seansta aynı hata: önceki açıklama yeniden kullanılır (yeni çağrı yok)
//   - seans başına en fazla AiGateway:MaxPerSession açıklama
//   - bütün sistemde dakikada en fazla AiGateway:MaxPerMinute çağrı
//   - aynı anda en fazla AiGateway:MaxConcurrent çağrı (sıra beklenmez; dolunca hazır metin)
public sealed class AiLimiter
{
    private readonly int _perSession, _perMinute;
    private readonly SemaphoreSlim _concurrent;
    private readonly ConcurrentDictionary<string, int> _sessionCalls = new();
    private readonly ConcurrentDictionary<string, AiFeedbackResult> _cache = new();
    private readonly ConcurrentQueue<DateTime> _recent = new();
    private readonly object _minuteLock = new();

    public AiLimiter(IConfiguration config)
    {
        _perSession = config.GetValue("AiGateway:MaxPerSession", 25);
        _perMinute = config.GetValue("AiGateway:MaxPerMinute", 60);
        _concurrent = new SemaphoreSlim(config.GetValue("AiGateway:MaxConcurrent", 4));
    }

    public static string Key(AiFeedbackRequest r)
        => $"{r.SessionId}|{r.StepId}|{r.Deviation.DeviationType}|{r.Deviation.Expected}|{r.Deviation.Actual}";

    public AiFeedbackResult? Cached(AiFeedbackRequest r) => _cache.GetValueOrDefault(Key(r));

    public void Remember(AiFeedbackRequest r, AiFeedbackResult result)
    {
        if (result.Source != "llm") return;
        if (_cache.Count >= MaxEntries) _cache.Clear();   // uzun açık kalan sunucuda bellek sınırlı kalsın
        _cache[Key(r)] = result;
    }

    private const int MaxEntries = 20_000;

    // Çağrı hakkı: null = izin var (Release çağrılmalı); değilse sınırın adı.
    public string? TryAcquire(string sessionId)
    {
        if (_sessionCalls.GetValueOrDefault(sessionId) >= _perSession) return "seans sınırı";
        lock (_minuteLock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (_recent.TryPeek(out var t) && t < cutoff) _recent.TryDequeue(out _);
            if (_recent.Count >= _perMinute) return "dakika sınırı";
            if (!_concurrent.Wait(0)) return "eşzamanlı çağrı sınırı";
            _recent.Enqueue(DateTime.UtcNow);
        }
        if (_sessionCalls.Count >= MaxEntries) _sessionCalls.Clear();
        _sessionCalls.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
        return null;
    }

    public void Release() => _concurrent.Release();
}
