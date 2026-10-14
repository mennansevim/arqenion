using System.Text;
using System.Text.Json;
using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

public sealed class AiFeedbackResult
{
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
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
}

// Motorun bulduğu bir sapma ve bağlamı (seans, olay, hasta, varsa açık komplikasyon).
// ProcedureModel: seansın modeli ("v1" ya da "v2.4"); Table: motorun bu sapma için hazır klinik metni (AI yoksa kullanılır).
public sealed record AiFeedbackRequest(string SessionId, string EventId, int StepId, DeviationDto Deviation, PatientProfile Patient,
    ActiveComplicationDto? Complication = null, string ProcedureModel = "v1", ClinicalKnowledgeEntry? Table = null);

public interface IAiFeedbackService
{
    Task<AiFeedbackResult?> GenerateAsync(AiFeedbackRequest request, CancellationToken ct);
}

// Karar motorda verilir; bu servis yalnızca açıklama metnini getirir:
// 1) ai-gateway POST /feedback (AiGateway:BaseUrl), AiGateway:TimeoutMs (varsayılan 8 sn; gateway kendi 6 sn yapay zekâ sınırı + 1 sn payla cevap verir) içinde cevap verirse onu kullanır.
// 2) Servis kapalıysa, hata verirse ya da zaman aşımına uğrarsa klinik bilgi tablosundaki (clinical-knowledge.json) metni kullanır.
public sealed class AiFeedbackService : IAiFeedbackService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ProcedureStore _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AiFeedbackService> _logger;
    private readonly AiLimiter _limiter;

    public AiFeedbackService(ProcedureStore store, IHttpClientFactory httpFactory,
        IConfiguration config, ILogger<AiFeedbackService> logger, AiLimiter limiter)
    {
        _limiter = limiter;
        _store = store;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<AiFeedbackResult?> GenerateAsync(AiFeedbackRequest request, CancellationToken ct)
    {
        var payload = new
        {
            sessionId = request.SessionId,
            eventId = request.EventId,
            stepId = request.StepId,
            deviationType = request.Deviation.DeviationType,
            // AI servisi tek bir beklenen kod bekler; motorun "A / B" listesinin ilki gönderilir (v1 çevirisi listeyi kendisi ayıklar).
            expected = request.ProcedureModel == "v1" ? request.Deviation.Expected : request.Deviation.Expected?.Split(" / ")[0],
            actual = request.Deviation.Actual,
            severity = request.Deviation.Severity,
            parameters = new Dictionary<string, string> { ["PATIENT_BODY_TYPE"] = request.Patient.BodyType },
            // "v1" ise gateway adım ve alet kodlarını v2.4'e çevirir (ai-gateway/app/legacy_v1_map.json).
            procedureModel = request.ProcedureModel,
            complication = request.Complication
        };
        var requestJson = JsonSerializer.Serialize(payload, RequestJsonOptions);

        var fallback = BuildTableResult(request);
        fallback.RequestJson = requestJson;

        var baseUrl = _config["AiGateway:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return fallback;

        // Aynı seansta aynı hata: önceki AI açıklaması (yeni çağrı ve maliyet yok).
        if (_limiter.Cached(request) is { } cached)
            return cached;
        if (_limiter.TryAcquire(request.SessionId) is { } limit)
        {
            _logger.LogInformation("AI çağrısı yapılmadı ({Limit}); klinik tablo kullanılıyor (session={SessionId}).", limit, request.SessionId);
            return fallback;
        }
        try
        {
            var result = await CallGatewayAsync(request, baseUrl, requestJson, fallback, ct);
            _limiter.Remember(request, result);
            return result;
        }
        finally
        {
            _limiter.Release();
        }
    }

    private async Task<AiFeedbackResult> CallGatewayAsync(AiFeedbackRequest request, string baseUrl, string requestJson,
        AiFeedbackResult fallback, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(_config.GetValue("AiGateway:TimeoutMs", 8000));

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var http = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/feedback")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            using var resp = await http.SendAsync(req, cts.Token);
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync(cts.Token);

            var gw = JsonSerializer.Deserialize<GatewayFeedback>(raw, JsonOptions);
            if (gw is null || string.IsNullOrWhiteSpace(gw.PossibleRisk)
                || string.IsNullOrWhiteSpace(gw.Explanation) || string.IsNullOrWhiteSpace(gw.RecommendedAction))
            {
                _logger.LogWarning("AI gateway eksik alanlı cevap verdi (step={Step}); klinik tablo kullanılıyor.", request.StepId);
                return fallback;
            }

            return new AiFeedbackResult
            {
                StepId = request.StepId,
                DeviationType = request.Deviation.DeviationType,
                PossibleRisk = gw.PossibleRisk,
                Explanation = gw.Explanation,
                RecommendedAction = gw.RecommendedAction,
                Severity = gw.Severity ?? request.Deviation.Severity,
                Source = gw.Source ?? "table",
                ModelName = gw.ModelName,
                PromptTokens = gw.PromptTokens,
                CompletionTokens = gw.CompletionTokens,
                TotalTokens = gw.PromptTokens + gw.CompletionTokens,
                RequestJson = requestJson,
                ResponseJson = raw
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Zaman aşımı, bağlantı hatası veya bozuk cevap: oyuncu cevapsız kalmasın, klinik tablo devreye girsin.
            _logger.LogWarning(ex, "AI gateway cevap vermedi (session={SessionId} step={Step}); klinik tablo kullanılıyor.",
                request.SessionId, request.StepId);
            return fallback;
        }
    }

    private AiFeedbackResult BuildTableResult(AiFeedbackRequest request)
    {
        var entry = request.Table ?? _store.GetClinicalEntry(request.StepId, request.Deviation.DeviationType);

        var result = new AiFeedbackResult
        {
            StepId = request.StepId,
            DeviationType = request.Deviation.DeviationType,
            PossibleRisk = entry?.PossibleRisk ?? "Prosedür kuralına aykırı bir işlem tespit edildi.",
            Explanation = entry?.Explanation ?? "Bu eylem kanonik prosedür sırasına veya alet uygunluğuna aykırıdır.",
            RecommendedAction = entry?.RecommendedAction ?? "Doğru adımı ve uygun aleti kontrol ederek işlemi tekrarlayın.",
            Severity = request.Deviation.Severity,
            Source = "table"
        };
        result.ResponseJson = JsonSerializer.Serialize(new { result.PossibleRisk, result.Explanation, result.RecommendedAction });
        return result;
    }

    // ai-gateway POST /feedback cevabındaki alanlar (camelCase; büyük/küçük harf duyarsız eşlenir).
    private sealed class GatewayFeedback
    {
        public string? PossibleRisk { get; set; }
        public string? Explanation { get; set; }
        public string? RecommendedAction { get; set; }
        public string? Severity { get; set; }
        public string? Source { get; set; }
        public string? ModelName { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
    }
}
