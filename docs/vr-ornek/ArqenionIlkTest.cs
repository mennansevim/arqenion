using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Arqenion ilk test: giriş → seans başlat → ilk olayı gönder → cevabı yaz.
// Boş bir sahnede bir GameObject'e ekleyin, Inspector'da kullanıcı adı/şifreyi girin, Play'e basın.
// Sonuçlar Console penceresine yazılır; aynı anda simülatörün "VR Test" ekranında (/sim/vr.html) adım adım görünür.
// Kılavuz: docs/vr-ilk-test.md
public class ArqenionIlkTest : MonoBehaviour
{
    [Header("Sunucu")]
    public string baseUrl = "https://setaceous-shriekingly-soo.ngrok-free.dev";

    [Header("Giriş (eğitmenin açtığı hesap)")]
    public string username = "vr-gelistirici";
    public string password = "";

    [Header("Senaryo")]
    public string scenarioId = "case-basic-01";
    // Sabit seed: cerrah 1. adımda hep aynı bistüriyi ister (test tekrarlanabilir olsun diye).
    public string seed = "ilk-test";

    [Header("VR Test ekranında görünecek ad (boşsa cihaz modeli)")]
    public string deviceLabel = "";

    private string token;
    private string deviceName => string.IsNullOrWhiteSpace(deviceLabel) ? SystemInfo.deviceModel : deviceLabel;

    private IEnumerator Start()
    {
        // 1) Giriş: kullanıcı adı + şifre → jeton
        LoginResponse login = null;
        yield return Post("/api/auth/login",
            JsonUtility.ToJson(new LoginRequest { username = username, password = password }),
            body => login = JsonUtility.FromJson<LoginResponse>(body));
        if (login == null) yield break;
        token = login.token;
        Debug.Log($"1) Giriş tamam: {login.user.username} ({login.user.role}). Jeton bitişi: {login.expiresAt}");

        // 2) Seans başlat → sessionId ve ilk adım
        SessionResponse session = null;
        yield return Post("/api/sessions",
            JsonUtility.ToJson(new StartSessionRequest { scenarioId = scenarioId, seed = seed }),
            body => session = JsonUtility.FromJson<SessionResponse>(body));
        if (session == null) yield break;
        var step = session.currentStep;
        Debug.Log($"2) Seans açıldı: {session.sessionId}  puan {session.score}\n" +
                  $"   Adım {step.nodeId}: {step.title}\n" +
                  $"   Cerrah istiyor: {step.surgeonRequest}\n" +
                  $"   Görev: {step.instruction}");

        // 3) İlk olay: kursiyer aleti cerraha uzattı.
        // Testte doğru aleti seçiyoruz; gerçekte kod kursiyerin elindeki aletten gelir.
        var ev = new EventRequest
        {
            eventId = Guid.NewGuid().ToString(),   // her olaya yeni id; ağ hatasında aynı id ile tekrar
            eventType = "instrument_delivered",
            instrumentCode = step.allowedInstruments[0],
            target = "SURGEON",
            timestamp = DateTime.UtcNow.ToString("o"),
        };
        EventResponse res = null;
        yield return Post($"/api/sessions/{session.sessionId}/events", JsonUtility.ToJson(ev),
            body => res = JsonUtility.FromJson<EventResponse>(body));
        if (res == null) yield break;

        // 4) Cevap: karar backend'den gelir, VR yalnız gösterir.
        if (res.success)
        {
            Debug.Log($"3) Doğru: {res.message}  puan {res.score}\n" +
                      $"   Sıradaki adım {res.nextStep.nodeId}: {res.nextStep.title}\n" +
                      $"   Görev: {res.nextStep.instruction}");
        }
        else
        {
            var risk = res.complication != null ? res.complication.possibleRisk : "";
            Debug.LogWarning($"3) Yanlış: {res.message}  puan {res.score}\n" +
                             $"   Beklenen {res.deviation?.expected}, verilen {res.deviation?.actual}\n" +
                             $"   Olası risk: {risk}\n" +
                             $"   Tekrar denenebilir: {res.allowRetry}");
        }
        Debug.Log("İlk test bitti.");
    }

    private IEnumerator Post(string path, string json, Action<string> onOk)
    {
        using (var req = new UnityWebRequest(baseUrl + path, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("ngrok-skip-browser-warning", "1");   // ngrok uyarı sayfasını atlar
            req.SetRequestHeader("X-Arqenion-Device", deviceName);    // VR Test ekranında cihaz bu adla görünür
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", "Bearer " + token);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{path} → {req.responseCode} {req.error}\n{req.downloadHandler.text}");
                yield break;
            }
            onOk(req.downloadHandler.text);
        }
    }

    // ----- JSON sınıfları (alan adları backend'deki JSON ile aynı) -----

    [Serializable] public class LoginRequest { public string username; public string password; }
    [Serializable] public class LoginResponse { public string token; public string expiresAt; public UserInfo user; }
    [Serializable] public class UserInfo { public string username; public string name; public string role; }

    [Serializable] public class StartSessionRequest { public string scenarioId; public string seed; }
    [Serializable] public class SessionResponse { public string sessionId; public int score; public StepView currentStep; }

    [Serializable]
    public class StepView
    {
        public string nodeId;
        public int stepId;
        public string kind;            // delivery | confirm | count | checklist
        public string expectedEvent;
        public string title;
        public string instruction;
        public string surgeonRequest;
        public List<string> allowedInstruments;   // doğru cevap: kursiyere gösterilmez
    }

    // Kullanılmayan alan eklemeyin: JsonUtility boş alanı "" diye gönderir, backend boş kodu 400 ile reddeder.
    [Serializable]
    public class EventRequest
    {
        public string eventId;
        public string eventType;
        public string instrumentCode;
        public string target;
        public string timestamp;
    }

    [Serializable]
    public class EventResponse
    {
        public bool success;
        public int score;
        public string message;
        public bool allowRetry;
        public bool completed;
        public string currentNodeId;
        public string nextNodeId;
        public Deviation deviation;
        public Complication complication;
        public StepView nextStep;
    }

    [Serializable] public class Deviation { public string deviationType; public string expected; public string actual; public string severity; }
    [Serializable] public class Complication { public string possibleRisk; public string explanation; public string recommendedAction; }
}
