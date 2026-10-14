# Arqenion — VR Cerrahi Eğitim Simülasyonu (MVP)

Açık apendektomi prosedüründe **ameliyathane teknikeri (scrub)** eğitimi için geliştirilen adaptif VR eğitim
sisteminin backend + simülatör MVP'si. Sistem, kullanıcının aksiyonlarını **deterministik** olarak doğrular,
puanlar ve hata durumunda **LLM tabanlı** pedagojik geri bildirim üretir.

## Mimari

```
Unity/Web Simülatör
   │  event (REST)                 ▲ aiFeedback (SignalR, async)
   ▼                               │
ASP.NET Core Backend
 ├─ ProcedureService   → deterministik doğrulama (ms)     [güvenlik-kritik]
 ├─ ScoringService     → puanlama (ms)
 ├─ AiFeedbackService  → klinik tablo (birincil) + LLM cila (async)   [açıklama]
 └─ PostgreSQL / InMemory
```

**Temel ilke:** Güvenlik-kritik "doğru mu / yanlış mı" kararı deterministik katmanda verilir; LLM yalnızca
tespit edilen hatanın klinik sonucunu ve doğru müdahaleyi açıklar. Bu ayrım demo güvenilirliğini ve
açıklanabilirliği garanti eder (LLM erişilemese bile çekirdek döngü çalışır).

## Klasör yapısı

```
backend/
  Controllers/SessionController.cs     # REST uçları
  Services/ProcedureService.cs         # deterministik doğrulama (çekirdek)
  Services/ScoringService.cs           # puanlama
  Services/AiFeedbackService.cs        # klinik tablo + opsiyonel OpenAI
  Services/ProcedureStore.cs           # JSON konfigürasyon yükleyici
  Hubs/SimulationHub.cs                # SignalR (canlı geri bildirim)
  Data/ArqenionDbContext.cs             # EF Core (5 tablo)
  Models/                              # config, entity, DTO
  Procedures/
    open-appendectomy-v1.json          # 22 ana aşama / 25 adım + 5 komplikasyon + malzeme kataloğu
    scenarios.json                     # standart + yapışıklıklı vaka
    clinical-knowledge.json            # hata→komplikasyon→müdahale tablosu (RAG-lite)
simulator/
  index.html, app.js, styles.css       # Unity olmadan tüm döngüyü test eden web arayüzü
docker-compose.yml                     # PostgreSQL
procedure-model/                       # 60 adımlı akış modeli v2.4 + RAG parçaları + akış motoru (bkz. procedure-model/README.md, ROADMAP.md)
```

## Çalıştırma

### 1. Backend (varsayılan: InMemory DB, sıfır kurulum)

```bash
cd backend
dotnet run
```

Backend `http://localhost:5080` üzerinde açılır. Swagger: `http://localhost:5080/swagger`.

### 2. Simülatör

```bash
cd simulator
python3 -m http.server 8000
```

Tarayıcıda `http://localhost:8000` → "Seansı Başlat" → alet butonlarıyla event gönder.

### 3. (Opsiyonel) PostgreSQL ile

```bash
docker compose up -d
```

`appsettings.json` içinde `Database:Provider` değerini `Postgres` yapın (varsayılan zaten Postgres;
Development ortamında InMemory'e düşer).

### 4. (Opsiyonel) LLM geri bildirimi

`appsettings.json` → `OpenAI:ApiKey` girildiğinde AI geri bildirimi, klinik tablo içeriğini GPT-4o mini ile
Türkçe olarak yeniden ifade eder. Anahtar boşsa doğrudan tablo içeriği kullanılır (kaynak izlenebilir kalır).
**API anahtarını repoya commit etmeyin** — ortam değişkeni veya user-secrets kullanın.

## API sözleşmesi

| Uç | Açıklama |
|---|---|
| `GET /api/procedure` | Adım listesi + malzeme/sütür kataloğu + komplikasyon tanımları (istemci kopya tanım tutmaz) |
| `POST /api/sessions` | Seans başlatır, ilk adımı döner |
| `POST /api/sessions/{id}/events` | Event doğrular, anlık sonuç döner; hata varsa AI feedback SignalR ile gelir |
| `POST /api/sessions/{id}/complete` | Seansı bitirir, rapor döner |
| `GET /api/sessions/{id}` | Seans durumunu döner |
| Hub `/hubs/simulation` | `JoinSession`, dinle: `AiFeedback`, `ScoreUpdate` |

## Komplikasyon kurgusu

Kaynak dokümanda kırmızı ile işaretlenen olaylar **komplikasyon**, yeşil ile işaretlenenler **tekniker çözümü**
olarak modellenmiştir. Komplikasyon, belirli bir adımda belirli bir **yanlış malzeme seçimiyle** tetiklenir ve
teknikerin **sırayla** teslim etmesi gereken bir çözüm zinciri açar. Zincir bitmeden asıl adıma dönülemez.

| # | Aşama (adım) | Tetikleyici seçim | Komplikasyon | Çözüm zinciri |
|---|---|---|---|---|
| K1 | 1. Cilt ve Karın Duvarına Giriş (adım 1) | No:11 / No:20 / kirli uç | Ciltte penetran kanama | Monopolar koter kalemi ile hemostaz (1 basamak) |
| K2 | 1. Cilt ve Karın Duvarına Giriş (adım 6) | Mayo makası | Kas yırtılması ve kanama | İzotonik yıkama → aspirasyon → bipolar koter (3) |
| K3 | 3. Çekuma Ulaşılması (adım 15) | Allis dişli / Ochsner-Kocher / Pean | Çekum yırtılması, batına sızma | Atravmat klemp → Savlon → aspirasyon → sıcak izotonik → aspirasyon → 3/0 Vicryl onarım → yıkama → aspirasyon → örtü+masa değişimi (9) |
| K4 | 10. Appendiksin Kesilmesi (adım 31) | Kullanılmış No:15 uç | Enfeksiyon riski | **Tekniker müdahalesi yok** — anestezi proflaktik antibiyotik uygular; adım yeni uçla tekrarlanır |
| K5 | 17. Peritonun Kapatılması (adım 49) | 4/0 Vicryl (ince sütür) | Periton yırtığı | Portegü + 0 Vicryl ile onarım (1) |

Zincirde yanlış malzeme verilirse `COMPLICATION_MISMANAGED` sapması üretilir ve beklenen malzeme gösterilir.

## Malzeme kataloğu

`instrumentCatalog`, **Karaoğlu Genel Cerrahi Apendektomi Seti**'nin 30 kalemiyle birebir eşlenmiştir
(`setCode` = ürün kodu, `quantity` = set adedi, `inSet: true`). Sette bulunmayan sarf ve cihaz malzemeleri
(bistüri uçları, makaslar, koter, aspiratör, enjektör, spanç, solüsyonlar) `inSet: false` ile işaretlenir.

Cihazlar iki parça olarak modellenmiştir: **cihaz gövdesi steril değildir** (`Cihaz` kategorisi,
`sterile: false`), sahaya yalnızca **steril uç** verilir (`Cihaz Ucu`) — monopolar/bipolar koter kalemi ve
Yankauer aspiratör ucu. Steril olmayan bir malzeme cerraha verilirse `STERILE_VIOLATION` üretilir.

## Yol haritası

- **Faz 1 (bu MVP):** GPT-4o mini + klinik tablo (RAG-lite), deterministik doğrulama, web simülatör, Unity PC/VR.
- **Faz 2:** Uzman onaylı etiketli veriyle Qwen/Llama sınıfı yerel modele geçiş (QLoRA), çevrimdışı çalışma.

Model bağımlılığı `IAiFeedbackService` arkasında soyutlanmıştır; Faz 2 geçişi tek implementasyon değişikliğidir.
