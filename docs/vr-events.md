# VR olay sözleşmesi (60 adımlık model)

VR gözlük ile backend arasındaki tek sözleşme budur. VR yalnızca **JSON olay** gönderir; doğru/yanlış, puan, sıradaki adım ve dal kararını backend verir. Web simülatörü (`simulator/`) aynı sözleşmeyle konuşur; VR'ın yapacağı her şey simülatörde ve `scripts/vr-replay.mjs` ile birebir denenebilir.

## Akış

1. `GET /api/procedure` → katalog ve adım listesi (VR tepsisini bu katalogla kurar).
2. `POST /api/sessions` → seans başlar, ilk adım (`currentStep`) gelir.
3. SignalR: `/hubs/simulation` bağlantısı, `JoinSession(sessionId)`.
4. Her kursiyer hareketi: `POST /api/sessions/{sessionId}/events` → cevap **hemen** gelir (karar + sıradaki adım).
5. AI açıklaması SignalR `AiFeedback` ile **birkaç saniye sonra** gelir; puan değişince `ScoreUpdate`.
6. Bitiş: `POST /api/sessions/{sessionId}/complete` → rapor.

## Seans başlatma

```http
POST /api/sessions
{ "userId": "kursiyer-42", "scenarioId": "case-bleeding-01", "seed": "opsiyonel" }
```

- `scenarioId`: `case-basic-01`, `case-adhesion-01`, `case-bleeding-01`, `case-count-01`, `case-obese-01`, `case-full-01` (ameliyat öncesi PRE01–PRE09 dahil).
- İstek `Authorization: Bearer <jeton>` taşır (`POST /api/auth/login` ile alınır; seans girişi yapan kullanıcıya aittir).
- `seed` (isteğe bağlı): aynı seed aynı cerrah istemlerini verir. Kayıtlı oturumları tekrar oynatırken kullanılır; gerçek derste gönderilmez.

Cevap: `sessionId`, `procedureModel` (`"v2.4"`), `currentStep` (aşağıdaki **Adım görünümü**), `score`.

## Olay

```json
{
  "eventId": "6f1c…-uuid",
  "eventType": "instrument_delivered",
  "instrumentCode": "NEEDLE_HOLDER_DEBAKEY_18",
  "sutureCode": "SUTURE_POLYGLACTIN_0",
  "target": "SURGEON",
  "counts": null,
  "timestamp": "2026-10-14T09:12:30.120Z"
}
```

| Alan | Zorunlu | Açıklama |
|---|---|---|
| `eventId` | evet | Her olay için yeni UUID. **Aynı olayı yeniden gönderirken aynı id** kullanılır: backend ikinci kez işlemez (ağ kesintisinde güvenli tekrar). |
| `eventType` | evet | Aşağıdaki türlerden biri. |
| `instrumentCode` | teslimde | Katalogdaki kod (`instrumentCatalog`). İplik tek başına uzatılıyorsa `sutureCode` yerine buraya da yazılabilir. |
| `sutureCode` | iplikli adımda | Katalogdaki iplik kodu (`sutureCatalog`); portegüye takılı iplik. |
| `target` | hayır | `SURGEON` (varsayılan), `DIRTY_AREA`, `PATHOLOGY_CONTAINER`. |
| `counts` | sayımda | `{ "COMPRESS": 9 }`; 44–46. adımlarda yalnız o adımın kategorisi yeterli. |
| `checkedItems` | kontrol listesinde | İşaretlenen madde kimlikleri, ör. `["PRE01.1", "PRE01.2", "PRE01.4"]` (maddeler adım görünümündeki `checklist`). |
| `timestamp` | hayır | Cihaz saati (kayıt için). |

### Olay türleri

| `eventType` | Ne zaman | Adım türü |
|---|---|---|
| `instrument_delivered` | Alet/iplik cerraha uzatıldı | `kind: "delivery"` |
| `step_confirmed` | Cerrahın yaptığı adım izlendi, yeni alet verilmedi | `kind: "confirm"` |
| `count_confirmed` | Sayım sonucu bildirildi | `kind: "count"` |
| `checklist_confirmed` | Ameliyat öncesi bölümün kontrol listesi onaylandı | `kind: "checklist"` |
| `hint_requested` | Kursiyer ipucu istedi (−2 puan) | her adım |
| `sterile_area_violated` | Steril alan ihlali algılandı | her adım |
| `instrument_dropped` | Alet düştü | her adım |
| `instrument_placed` | (eski model) kirli alana / patoloji kabına bırakma | — |

Yanlış türde olay (ör. onay adımında alet uzatmak) **kursiyer hatasıdır**: 200 döner, `deviation` doludur, puan düşer.

### Sözleşme hatası (400)

Şunlar **entegrasyon hatasıdır**, kursiyerin hatası sayılmaz, kayda ve puana girmez:

- bilinmeyen `eventType` ya da `target`,
- katalogda olmayan `instrumentCode` / `sutureCode`,
- teslim olayında kod yok,
- sayım olayında `counts` yok, bilinmeyen kategori ya da negatif sayı,
- kontrol listesi olayında `checkedItems` yok ya da bilinmeyen madde kimliği var.

```json
{ "message": "Bilinmeyen olay türü: 'instrument_give'.", "field": "eventType", "allowed": ["instrument_delivered", "…"] }
```

## Cevap (200)

```json
{
  "success": false,
  "currentNodeId": "52", "nextNodeId": "52",
  "currentStepId": 52, "nextStepId": 52,
  "score": 92,
  "deviation": { "deviationType": "WRONG_INSTRUMENT", "expected": "SUTURE_POLYGLACTIN_0", "actual": "SUTURE_POLYPROPYLENE_0", "severity": "MEDIUM" },
  "message": "Bu aşamada yanlış iplik seçildi.",
  "allowRetry": true, "completed": false,
  "nextStep": { "…": "Adım görünümü" },
  "complication": { "possibleRisk": "…", "explanation": "…", "recommendedAction": "…", "source": "table" },
  "cumulativeComplication": { "level": "MEDIUM", "riskScore": 44, "items": [ "…" ] },
  "branch": null,
  "adaptive": null
}
```

- `currentNodeId` / `nextNodeId`: düğüm kimliği (`"43a"`, `"47b"` dahil). `*StepId` ana adım numarasıdır (47b → 47).
- `deviation`: yalnız hatada. `deviationType`: `WRONG_INSTRUMENT`, `WRONG_ORDER`, `STERILE_VIOLATION`, `COUNT_MISMATCH`, `INSTRUMENT_DROPPED`, `CHECKLIST_INCOMPLETE` (kontrol listesinde zorunlu madde atlandı; `expected` atlanan madde kimlikleri).
- `complication`: hatanın onaylı senaryodan klinik kartı (anında). Yapay zekânın açıklaması ayrıca SignalR ile gelir.
- `branch`: karar noktasında dala girildi (`resolved: false`) ya da daldan ana akışa dönüldü (`resolved: true`): `{ condition, title, source, fromNodeId, toNodeId, resolved }`.
- `adaptive`: bu hata bir koşulu doğru yaptı; dal ileride karar adımında açılacak: `[{ condition, title, message, evaluatedAt, outcome, scenarioId }]`. `outcome: true` ise akış değişmez, rapora yazılır (ör. enfeksiyon riski).

## Adım görünümü (`currentStep`, `nextStep`)

| Alan | Açıklama |
|---|---|
| `nodeId`, `stepId` | Düğüm ve ana adım |
| `kind` | `delivery`, `confirm`, `count`, `checklist` |
| `expectedEvent` | Beklenen `eventType` |
| `allowedInstruments` | Cerrah istemi uygulanmış kabul edilen aletler |
| `requiredSutures`, `requiredSuture` | Kabul edilen iplikler |
| `surgeonRequest` | Cerrahın istediği (hekim tercihi adımlarında); VR cerrahın sesi/altyazısı olarak gösterir |
| `clamps` | Damar sayısına bağlı kalan klempler (`[{code, qty}]`) |
| `countCategory`, `tableCount`, `recordedCount` | Sayım adımında kategori, masadaki gerçek sayı, başlangıç kaydı |
| `checklist` | Ameliyat öncesi maddeler: `[{ id, name, task, optional }]`; `optional` koşullu maddedir (zorunlu değil) |
| `isBranch`, `condition`, `conditionText` | Dal içindeyse koşul ve açıklaması |
| `instruction`, `clinicalNote`, `title`, `phase`, `phaseNo` | Görev metni |

## SignalR

- Hub: `/hubs/simulation?access_token=<jeton>`. Bağlandıktan sonra `JoinSession(sessionId)` (yalnız kendi seansı).
- `ScoreUpdate`: `{ sessionId, score }`.
- `AiFeedback`: `{ stepId, deviationType, possibleRisk, explanation, recommendedAction, severity, source: "llm" }`. Yalnızca yapay zekâ yazdığında ve kontrolden geçtiğinde gelir; gelmezse `complication` kartı yeterlidir.

## Tekrar ve sıra

- Ağ hatasında aynı `eventId` ile yeniden gönderin; cevap "yinelenen" mesajıyla mevcut durumu verir, olay ikinci kez sayılmaz.
- Olaylar sırayla gönderilmelidir (bir cevap gelmeden sıradakini göndermeyin): karar her olayda bir önceki duruma dayanır.

## Test

- **Kayıtlı oturumlar:** `backend.Tests/VrSessions/*.json` — gönderilen her JSON olay ve beklenen cevap. `dotnet test backend.Tests` bunları süreç içi HTTP ile tekrar oynatır.
- **Canlı sunucuya karşı:** `node scripts/vr-replay.mjs replay backend.Tests/VrSessions/kanama.json --url http://localhost:5080`
- **Yeni kayıt:** `node scripts/vr-replay.mjs record scripts/vr-plans/kanama.json backend.Tests/VrSessions/kanama.json`
