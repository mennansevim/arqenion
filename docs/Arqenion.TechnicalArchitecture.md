# Arqenion Technical Architecture

## 1. Amaç

Bu doküman, Arqenion AI destekli VR cerrahi eğitim sisteminin teknik mimarisini tanımlar.

Sistem; VR ortamında gerçekleştirilen kullanıcı hareketlerini event tabanlı olarak işler, cerrahi prosedür adımlarını deterministik olarak doğrular, kullanıcı performansını puanlar, hata ve komplikasyon akışlarını yönetir, eğitim geçmişini merkezi olarak saklar ve yapay zekâ ile pedagojik geri bildirim ve adaptif eğitim sağlar.

İlk klinik senaryo açık apendektomi prosedürüdür. Sistem yeni cerrahi prosedürlerin aynı mimari üzerine parametrik olarak eklenebilmesini hedefler.

---

# 2. Temel Mimari Prensipler

1. **VR istemcisi doğrudan Cloud AI ile konuşmaz.**
2. **VR ile Edge katmanı arasında event tabanlı JSON trafik kullanılır.**
3. **Gerçek zamanlı ve güvenlik-kritik kararlar lokal Edge üzerinde deterministik olarak verilir.**
4. **Redis lokal state, cache, event buffer ve pending-sync katmanı olarak kullanılır.**
5. **PostgreSQL'in ana ve kalıcı kopyası Cloud tarafında tutulur.**
6. **Cloud PostgreSQL sistemin source-of-truth veritabanıdır.**
7. **AI modeli doğru/yanlış klinik kararın ana kaynağı değildir.**
8. **AI; açıklama, pedagojik geri bildirim, kullanıcı analizi ve adaptif eğitim için kullanılır.**
9. **İnternet bağlantısı geçici olarak kesilse bile aktif eğitim seansı mümkün olduğunca devam eder.**
10. **Tüm şirket, lisans, kullanıcı, cihaz ve eğitim verileri multi-tenant olarak yönetilir.**

---

# 3. Yüksek Seviye Sistem Mimarisi

```text
┌─────────────────────────────────────────────────────┐
│                    VR HEADSET                       │
│              Unreal Engine / OpenXR                 │
│                                                     │
│  - Sanal ameliyathane                               │
│  - El takibi / Controller                           │
│  - Kullanıcı etkileşimleri                          │
│  - Cerrahi ekipman etkileşimleri                    │
└──────────────────────┬──────────────────────────────┘
                       │
                       │ JSON Event
                       │ HTTPS / WebSocket
                       ▼
┌─────────────────────────────────────────────────────┐
│              ARQENION EDGE DEVICE                   │
│               Raspberry Pi 5                        │
│                 Docker                              │
│                                                     │
│  ┌───────────────────────────────────────────────┐  │
│  │ Edge API                                      │  │
│  └───────────────────────────────────────────────┘  │
│                      │                              │
│       ┌──────────────┼───────────────┐              │
│       ▼              ▼               ▼              │
│ Procedure Engine  Scoring Engine  Complication      │
│                                  Engine             │
│       │              │               │              │
│       └──────────────┼───────────────┘              │
│                      ▼                              │
│                   Redis                             │
│                                                     │
│  - Active Session State                             │
│  - Cache                                            │
│  - Event Buffer                                     │
│  - Pending Cloud Sync                               │
│  - License Cache                                    │
└──────────────────────┬──────────────────────────────┘
                       │
                       │ HTTPS / Secure API
                       ▼
┌─────────────────────────────────────────────────────┐
│                  ARQENION CLOUD                     │
│                                                     │
│  Cloud Backend                                      │
│      │                                              │
│      ├────────────── PostgreSQL                     │
│      │                                              │
│      ├────────────── Cloud AI / LLM                 │
│      │                                              │
│      ├────────────── Object Storage                 │
│      │                                              │
│      └────────────── Admin / Analytics              │
└─────────────────────────────────────────────────────┘
```

---

# 4. Fiziksel Bileşenler

## 4.1. VR Headset

### Amaç

Cerrahi eğitim simülasyonunun kullanıcıya sunulduğu ana istemci cihazıdır.

### Sorumlulukları

- Unreal Engine tabanlı VR uygulamasını çalıştırmak
- OpenXR üzerinden VR donanım entegrasyonunu yönetmek
- El takibi ve controller hareketlerini algılamak
- Kullanıcının cerrahi ekipmanlarla etkileşimlerini algılamak
- Klinik ortamı ve cerrahi senaryoyu görselleştirmek
- Kullanıcı aksiyonlarını event olarak Edge API'ye göndermek
- Edge API'den gelen işlem sonuçlarını uygulamak
- Skor ve hata bildirimlerini kullanıcıya göstermek
- AI geri bildirimlerini kullanıcıya sunmak

### VR'ın Tutmaması Gereken Veriler

VR cihazı kalıcı kullanıcı veritabanı olarak kullanılmamalıdır.

VR üzerinde yalnızca:

- aktif session kimliği
- geçici kullanıcı state'i
- görsel senaryo state'i
- lokal assetler

tutulmalıdır.

---

# 5. Arqenion Edge Device

## 5.1. Donanım

Önerilen başlangıç donanımı:

- Raspberry Pi 5
- 8 GB veya 16 GB RAM
- NVMe SSD
- Gigabit Ethernet
- Yerel ağ bağlantısı
- Docker / Docker Compose

Edge cihazı ağır Cloud AI inference yapmak için değil, düşük gecikmeli lokal servisleri çalıştırmak için kullanılır.

---

# 6. Edge API

## 6.1. Amaç

VR uygulamasının Arqenion sistemine giriş noktasıdır.

VR hiçbir çekirdek servisle doğrudan haberleşmez. Tüm eventler Edge API üzerinden sisteme girer.

## 6.2. Sorumlulukları

- VR'dan gelen JSON eventlerini kabul etmek
- Event formatını doğrulamak
- Session kimliğini kontrol etmek
- Kullanıcı ve cihaz yetkisini doğrulamak
- Lisans bilgisini kontrol etmek
- Eventi Procedure Engine'e iletmek
- Scoring Engine sonucunu almak
- Complication Engine sonucunu almak
- Redis state'ini güncellemek
- VR'a düşük gecikmeli cevap dönmek
- Gerekli eventleri Cloud'a göndermek
- AI feedback işlemini asenkron başlatmak
- İnternet yoksa eventleri pending-sync kuyruğuna almak

## 6.3. Örnek Event

```json
{
  "eventId": "evt-39403",
  "sessionId": "ses-123",
  "userId": "usr-51",
  "procedure": "open-appendectomy",
  "procedureVersion": "1.0",
  "step": 15,
  "action": "INSTRUMENT_SELECTED",
  "instrument": "BABCOCK",
  "timestamp": "2027-07-10T11:24:31Z"
}
```

## 6.4. Örnek Response

```json
{
  "accepted": true,
  "valid": true,
  "sessionId": "ses-123",
  "currentStep": 15,
  "nextStep": 16,
  "score": 86,
  "complication": null,
  "state": "PROCEDURE_CONTINUE"
}
```

---

# 7. Procedure Engine

## 7.1. Amaç

Cerrahi prosedürün hangi aşamada olduğunu ve gelen kullanıcı aksiyonunun mevcut prosedür state'i açısından geçerli olup olmadığını belirler.

## 7.2. Sorumlulukları

- Aktif prosedür versiyonunu yüklemek
- Mevcut prosedür adımını belirlemek
- Beklenen kullanıcı aksiyonunu belirlemek
- Beklenen cerrahi ekipmanı belirlemek
- Event sırasını doğrulamak
- Ön koşulları kontrol etmek
- Prosedür dallanmalarını yönetmek
- Bir sonraki adıma geçişi belirlemek
- Senaryo state-machine yapısını yönetmek
- Yeni prosedürlerin parametrik olarak eklenmesini sağlamak

## 7.3. Girdiler

- sessionId
- procedureId
- procedureVersion
- currentStep
- action
- instrument
- complicationState

## 7.4. Çıktılar

- VALID_ACTION
- INVALID_ACTION
- WRONG_INSTRUMENT
- WRONG_SEQUENCE
- STEP_COMPLETED
- PROCEDURE_COMPLETED
- COMPLICATION_TRIGGER

## 7.5. Veri Kaynağı

Procedure Engine klinik iş akışını parametrik JSON/config modellerinden okuyabilir.

Örneğin:

```json
{
  "stepId": 15,
  "name": "Çekumun tutulması",
  "expectedAction": "GIVE_INSTRUMENT",
  "expectedInstrument": "BABCOCK",
  "nextStep": 16
}
```

---

# 8. Scoring Engine

## 8.1. Amaç

Kursiyerin eğitim performansını gerçek zamanlı olarak değerlendirmek ve puanlamaktır.

## 8.2. Sorumlulukları

- Doğru işlem puanlaması
- Yanlış işlem cezası
- Yanlış ekipman cezası
- İşlem sırası değerlendirmesi
- Süre değerlendirmesi
- Komplikasyon performansı
- Sterilite kurallarının değerlendirilmesi
- Tekrarlanan hata cezası
- Eğitim sonunda final score oluşturulması

## 8.3. Örnek Puan Kategorileri

```text
Instrument Selection
Procedure Order
Response Time
Complication Handling
Sterility
Procedure Knowledge
```

## 8.4. Örnek Sonuç

```json
{
  "sessionId": "ses-123",
  "overallScore": 84,
  "categories": {
    "instrumentSelection": 91,
    "procedureOrder": 88,
    "responseTime": 73,
    "complicationHandling": 67,
    "sterility": 100
  }
}
```

Bu veri eğitim tamamlandığında Cloud PostgreSQL'e kalıcı olarak yazılır.

---

# 9. Complication Engine

## 9.1. Amaç

Kullanıcının yanlış hareketlerinden oluşabilecek klinik komplikasyonları deterministik olarak yönetmektir.

## 9.2. Sorumlulukları

- Komplikasyon tetikleme koşullarını kontrol etmek
- Yanlış ekipman kullanımından doğan komplikasyonları başlatmak
- Ana prosedürü geçici olarak durdurmak
- Komplikasyon çözüm zincirini başlatmak
- Beklenen müdahaleleri sırayla doğrulamak
- Yanlış komplikasyon müdahalesini tespit etmek
- Komplikasyon çözülünce ana prosedüre dönmek
- Komplikasyon sonucunu Scoring Engine'e iletmek

## 9.3. Örnek Akış

```text
STEP 15
  │
  ├── BABCOCK
  │      └── doğru → STEP 16
  │
  └── Travmatik klemp
         │
         ▼
  COMPLICATION_TRIGGER
         │
         ▼
  Çekum yaralanması
         │
         ▼
  Complication Workflow
         │
         ▼
  Müdahaleler tamamlandı
         │
         ▼
  STEP 15 / STEP 16
```

AI modeli bu komplikasyonu üretmez.

Komplikasyonun oluşup oluşmadığı deterministik Complication Engine tarafından belirlenir.

---

# 10. Redis

## 10.1. Amaç

Raspberry Pi üzerinde gerçek zamanlı state, cache ve bağlantı kesintisi toleransı sağlamaktır.

Redis ana kalıcı veritabanı değildir.

## 10.2. Redis'te Tutulacak Veriler

### Active Session State

```text
session:{sessionId}
```

Örnek:

```json
{
  "userId": "usr-51",
  "organizationId": "org-12",
  "procedure": "open-appendectomy",
  "procedureVersion": "1.0",
  "currentStep": 17,
  "score": 82,
  "activeComplication": null
}
```

### License Cache

```text
license:{organizationId}
```

İçerik:

- licenseStatus
- validUntil
- seatLimit
- deviceLimit
- concurrentSessionLimit
- lastValidatedAt

### Event Buffer

Cloud'a gönderilmeyi bekleyen eventler.

```text
events:pending-sync
```

### AI Feedback Cache

Aynı veya benzer context için kısa süreli cache.

### Procedure Cache

Aktif prosedür tanımlarının hızlı okunması.

## 10.3. Persistence

Redis için AOF persistence kullanılması önerilir.

Amaç:

- elektrik kesintisi
- servis restart
- kısa süreli cihaz problemi

durumlarında pending-sync eventlerinin kaybolma ihtimalini azaltmaktır.

---

# 11. Cloud Backend

## 11.1. Amaç

Arqenion platformunun merkezi iş katmanıdır.

## 11.2. Sorumlulukları

- Organization yönetimi
- Kullanıcı yönetimi
- Rol ve yetkilendirme
- Lisans yönetimi
- Cihaz yönetimi
- Merkezi eğitim geçmişi
- Eğitim raporları
- AI servis entegrasyonu
- Dijital kursiyer profili
- Adaptif eğitim
- Model yönetimi
- Audit log
- Admin panel backend
- Edge cihazlarının senkronizasyonu

---

# 12. PostgreSQL

## 12.1. Amaç

Sistemin ana kalıcı veri kaynağıdır.

```text
PostgreSQL = Source of Truth
```

Lokal Redis'te bulunan bilgiler geçicidir.

Cloud PostgreSQL'deki bilgiler kalıcı ve merkezi kayıttır.

---

# 13. Multi-Tenant Organization Modeli

Arqenion birden fazla hastane, üniversite veya eğitim kuruluşuna hizmet verebilir.

Bu nedenle sistem multi-tenant tasarlanmalıdır.

Ana yapı:

```text
Organization
   │
   ├── Licenses
   ├── Users
   ├── Devices
   ├── Training Sessions
   └── Reports
```

Ana iş tablolarının çoğunda:

```text
organization_id
```

bulunması önerilir.

---

# 14. Organization

## Amaç

Arqenion lisansı alan şirket, hastane, üniversite veya kurumu temsil eder.

## Önerilen Alanlar

```text
id
name
type
status
created_at
updated_at
```

---

# 15. User

## Amaç

Sistemi kullanan kursiyer, eğitmen, kurum yöneticisi ve sistem yöneticilerini temsil eder.

## Alanlar

```text
id
organization_id
email
name
surname
status
role
created_at
last_login_at
```

## Roller

```text
TRAINEE
INSTRUCTOR
ORGANIZATION_ADMIN
ARQENION_ADMIN
```

---

# 16. License

## 16.1. Amaç

Kuruma tanımlanan ticari kullanım haklarını yönetir.

## 16.2. Desteklenen Lisans Limitleri

- kullanıcı sayısı
- cihaz sayısı
- eş zamanlı session sayısı
- geçerlilik süresi
- kullanılabilir prosedürler
- paket özellikleri

## Örnek

```text
Organization: University Hospital A

Plan: Professional
Seat Limit: 50
Device Limit: 10
Concurrent Sessions: 5
Valid Until: 2028-01-01
```

## Önerilen Tablolar

```text
license_plans
licenses
license_assignments
```

---

# 17. License Validation Service

## Amaç

Bir kullanıcının eğitim başlatmaya hakkı olup olmadığını kontrol eder.

## Kontroller

```text
Organization aktif mi?
        │
        ▼
Lisans aktif mi?
        │
        ▼
Lisans süresi geçerli mi?
        │
        ▼
User seat mevcut mu?
        │
        ▼
Cihaz yetkili mi?
        │
        ▼
Concurrent session limiti uygun mu?
        │
        ▼
SESSION ALLOWED
```

Başarılı lisans kontrolü Edge Redis üzerinde kısa süreli cache edilebilir.

Bu sayede kısa internet kesintileri sırasında aktif eğitim kesilmez.

---

# 18. Device Management

## Amaç

Sisteme bağlı VR ve Edge cihazlarının merkezi yönetimini sağlar.

## Önerilen Tablo

```text
devices
```

## Alanlar

```text
id
organization_id
serial_number
device_type
status
software_version
last_seen
activated_at
```

## Device Type

```text
VR_HEADSET
EDGE_DEVICE
```

---

# 19. Procedure

Cerrahi prosedürün genel kimliğini temsil eder.

```text
procedures
```

Örnek:

```text
Open Appendectomy
```

---

# 20. Procedure Version

Prosedürlerin zaman içinde değişebilmesini sağlar.

```text
procedure_versions
```

Örnek:

```text
open-appendectomy-v1
open-appendectomy-v2
```

Bu sayede geçmiş eğitimler hangi klinik akışla gerçekleştirildiği bilgisiyle saklanabilir.

---

# 21. Scenario

Aynı prosedür için farklı eğitim senaryolarını tanımlar.

Örneğin:

```text
Standard Appendectomy
Appendectomy With Adhesion
Advanced Complication Scenario
```

---

# 22. Training Session

## Amaç

Bir kullanıcının gerçekleştirdiği tek bir eğitim oturumunu temsil eder.

## Alanlar

```text
id
organization_id
user_id
device_id
procedure_version_id
scenario_id
started_at
completed_at
final_score
duration
status
```

## Status

```text
CREATED
RUNNING
PAUSED
COMPLETED
FAILED
ABORTED
```

---

# 23. Session Events

Kullanıcının eğitim sırasında oluşturduğu anlamlı domain eventlerini saklar.

Örnek eventler:

```text
SESSION_STARTED
STEP_STARTED
INSTRUMENT_SELECTED
WRONG_INSTRUMENT
CORRECT_INSTRUMENT
STEP_COMPLETED
COMPLICATION_TRIGGERED
COMPLICATION_RESOLVED
SESSION_COMPLETED
```

Çok yüksek frekanslı el koordinatları gibi telemetry verilerinin doğrudan transaction PostgreSQL tablolarına yazılması önerilmez.

Bunlar gerekiyorsa Object Storage veya ayrı telemetry altyapısında tutulmalıdır.

---

# 24. Session Scores

Eğitim sırasında oluşan kategori bazlı skorları saklar.

```text
session_scores
```

Örnek:

```text
session_id
category
score
created_at
```

---

# 25. Session Complications

Kullanıcının hangi komplikasyonları tetiklediğini ve nasıl yönettiğini saklar.

```text
session_complications
```

Alanlar:

```text
session_id
complication_id
trigger_step
trigger_action
resolved
resolution_score
started_at
resolved_at
```

---

# 26. User Performance Profile

Kullanıcının uzun vadeli eğitim profilidir.

Bu yapı dijital kursiyer modelinin temelini oluşturur.

## İçerik

- prosedür bazlı başarı
- adım bazlı başarı
- ekipman seçim başarısı
- ortalama işlem süreleri
- en sık yapılan hatalar
- komplikasyon başarı oranı
- tekrar eden zayıflıklar
- gelişim trendi
- eğitim seviyesi

---

# 27. Digital Trainee / Digital Twin

## Amaç

Kursiyerin eğitim davranışını uzun vadeli olarak modellemektir.

## Sorumlulukları

- Kullanıcı geçmişini analiz etmek
- Güçlü yönleri belirlemek
- Zayıf yönleri belirlemek
- Hata örüntülerini tespit etmek
- Gelişim trendlerini hesaplamak
- Kişiye özel eğitim parametreleri üretmek

Örnek çıktı:

```json
{
  "userId": "usr-51",
  "procedure": "open-appendectomy",
  "level": "INTERMEDIATE",
  "weakAreas": [
    "instrument_selection",
    "complication_handling"
  ],
  "strongAreas": [
    "procedure_order",
    "sterility"
  ]
}
```

---

# 28. Adaptive Training Engine

## Amaç

Digital Trainee verisine göre eğitim deneyimini kişiselleştirmektir.

## Ayarlanabilecek Parametreler

- senaryo zorluğu
- komplikasyon sıklığı
- geri bildirim miktarı
- geri bildirim gecikmesi
- tekrar edilecek prosedür adımları
- sınav / eğitim modu
- yardım seviyesi

Örnek:

Kullanıcı sürekli olarak cerrahi ekipman seçiminde hata yapıyorsa sistem ilgili adımların tekrar sıklığını artırabilir.

---

# 29. Cloud AI / LLM Service

## Amaç

Deterministik motorun ürettiği sonucu klinik ve pedagojik olarak açıklamaktır.

AI temel klinik doğrulama motorunun yerine geçmez.

## Sorumlulukları

- hata açıklaması
- doğru işlemin açıklanması
- komplikasyonun klinik sonucunun açıklanması
- pedagojik geri bildirim
- eğitim sonrası özet
- performans analizi
- kişiye özel öneriler

## Örnek Context

```json
{
  "procedure": "open-appendectomy",
  "step": 15,
  "userAction": "PEAN_SELECTED",
  "expectedInstrument": "BABCOCK",
  "engineResult": "WRONG_INSTRUMENT",
  "complication": "CECUM_DAMAGE",
  "traineeLevel": "BEGINNER"
}
```

AI bu doğrulanmış context üzerinden açıklama üretir.

---

# 30. AI Model Management

## Amaç

Kullanılan AI modellerinin ve promptların izlenebilir şekilde yönetilmesidir.

## Tutulacak Bilgiler

```text
ai_model_versions
prompt_versions
ai_evaluations
```

Örnek:

```text
Model
Provider
Version
Prompt Version
Created At
Active
Benchmark Score
```

---

# 31. AI Benchmark

AI modelleri aşağıdaki kriterler açısından karşılaştırılır:

- klinik doğruluk
- pedagojik kalite
- hallucination oranı
- prosedür context uyumu
- komplikasyon context uyumu
- Türkçe performansı
- response latency
- inference maliyeti
- RAM / VRAM ihtiyacı
- model boyutu
- edge deployment uygunluğu
- cevap tutarlılığı

---

# 32. Faz 2 AI Eğitimi

Pilot kullanımlardan gerçek eğitim datası toplandıktan sonra:

```text
Pilot Data
   │
   ▼
Anonymization
   │
   ▼
Clinical Expert Labeling
   │
   ▼
Dataset Quality Control
   │
   ▼
Train / Validation / Test Split
   │
   ▼
Baseline Benchmark
   │
   ▼
QLoRA / Fine-Tuning
   │
   ▼
Fine-Tuned Benchmark
   │
   ▼
Model Selection
   │
   ▼
Quantization
   │
   ▼
Optional Edge Deployment
```

Qwen/Llama sınıfı lokal modeller bu fazda değerlendirilir.

---

# 33. Object Storage

PostgreSQL içerisine büyük binary dosyalar koyulmamalıdır.

Object Storage aşağıdaki içerikler için kullanılabilir:

- AI training datasetleri
- export dosyaları
- büyük log dosyaları
- VR session kayıtları
- video kayıtları
- model artifactleri
- benchmark çıktıları

PostgreSQL içerisinde yalnızca metadata ve object path tutulur.

---

# 34. Event Akışı

```text
User VR Action
      │
      ▼
VR Application
      │
      │ JSON EVENT
      ▼
Edge API
      │
      ▼
Procedure Engine
      │
      ├─────────────── invalid
      │                    │
      │                    ▼
      │             Complication Engine
      │                    │
      ▼                    ▼
Scoring Engine ◄───────────┘
      │
      ▼
Redis State Update
      │
      ├─────────────► VR Response
      │
      └─────────────► Cloud Sync
                            │
              ┌─────────────┼─────────────┐
              ▼             ▼             ▼
          PostgreSQL      Cloud AI     Analytics
```

---

# 35. Fast Path ve AI Path

Sistem iki farklı işlem hattına ayrılır.

## Fast Path

Gerçek zamanlı VR kararları:

```text
VR
 ↓
Edge API
 ↓
Procedure Engine
 ↓
Complication Engine
 ↓
Scoring
 ↓
Redis
 ↓
VR
```

Bu akış Cloud AI sonucunu beklemez.

## AI Path

```text
Validated Event
      │
      ▼
Cloud AI
      │
      ▼
Pedagogical Feedback
      │
      ▼
Edge / VR
```

AI cevabı asenkron gelir.

---

# 36. Offline Çalışma

İnternet bağlantısı kesildiğinde:

```text
VR
 ↓
Edge API
 ↓
Procedure / Scoring / Complication
 ↓
Redis
```

çalışmaya devam eder.

Cloud'a gidemeyen eventler:

```text
pending-sync
```

kuyruğunda tutulur.

İnternet geri geldiğinde:

```text
Pending Events
     │
     ▼
Cloud Sync Worker
     │
     ▼
Cloud Backend
     │
     ▼
PostgreSQL
```

şeklinde senkronize edilir.

---

# 37. Cloud Sync Worker

## Amaç

Edge cihazındaki tamamlanmamış cloud senkronizasyonlarını yönetmektir.

## Sorumlulukları

- pending eventleri okumak
- cloud bağlantısını kontrol etmek
- eventleri batch göndermek
- başarılı eventleri kuyruktan silmek
- hata durumunda retry uygulamak
- duplicate eventleri önlemek

Her event için benzersiz:

```text
eventId
```

kullanılması önerilir.

Cloud tarafında idempotency kontrolü uygulanmalıdır.

---

# 38. Docker Dağılımı

Raspberry Pi üzerinde önerilen yapı:

```text
docker-compose
│
├── arqenion-edge-api
│
├── arqenion-procedure-engine
│
├── arqenion-scoring-engine
│
├── arqenion-complication-engine
│
├── arqenion-sync-worker
│
└── redis
```

İlk MVP'de Procedure, Scoring ve Complication Engine fiziksel olarak Edge API ile aynı .NET uygulaması içinde de tutulabilir.

Mantıksal sınırlar korunmalı fakat gereksiz mikroservis karmaşıklığı yaratılmamalıdır.

---

# 39. Cloud Deployment

Önerilen başlangıç yapısı:

```text
Cloud
│
├── Arqenion Backend API
├── PostgreSQL
├── AI Gateway
├── AI Worker
├── Analytics Worker
├── Admin Backend
└── Object Storage
```

İlk MVP için Kubernetes zorunlu değildir.

Docker tabanlı deployment yeterlidir.

Sistem büyüdüğünde container orchestration katmanı eklenebilir.

---

# 40. Güvenlik

## Edge ↔ Cloud

- HTTPS
- cihaz kimlik doğrulaması
- rotating API credentials
- mümkünse device certificate

## User Authentication

- JWT / OAuth2
- refresh token
- role based access control

## Tenant Isolation

Her sorguda organization sınırı korunmalıdır.

## Sensitive Data

- kullanıcı bilgileri şifrelenmiş bağlantılar üzerinden taşınmalı
- password plaintext tutulmamalı
- AI API key Edge veya VR uygulamasına gömülmemeli
- secret management kullanılmalı

---

# 41. Audit

Aşağıdaki işlemler audit log oluşturmalıdır:

- kullanıcı oluşturma
- kullanıcı silme
- lisans değişikliği
- cihaz aktivasyonu
- procedure version değişikliği
- admin işlemleri
- model versiyon değişikliği
- yetki değişikliği

Önerilen tablo:

```text
audit_logs
```

---

# 42. Observability

Sistem aşağıdaki metrikleri toplamalıdır:

## Edge

- CPU
- RAM
- disk
- Redis usage
- pending sync count
- API latency
- error rate
- cloud connectivity

## Application

- active sessions
- completed sessions
- event processing latency
- complication rate
- AI response latency
- failed sync count

## Cloud

- API error rate
- PostgreSQL latency
- AI request count
- AI cost
- active organizations
- active devices

---

# 43. Temel PostgreSQL Entity Listesi

```text
organizations

users
organization_users

license_plans
licenses
license_assignments

devices
device_activations

procedures
procedure_versions

scenarios
scenario_versions

training_sessions
session_events
session_scores
session_complications

user_performance_profiles

ai_feedback
ai_model_versions
prompt_versions
ai_evaluations

audit_logs
```

---

# 44. Veri Saklama Kararı

| Veri | Sistem |
|---|---|
| Aktif session state | Edge Redis |
| Current procedure step | Edge Redis |
| Anlık skor | Edge Redis |
| Aktif komplikasyon | Edge Redis |
| License cache | Edge Redis |
| Pending Cloud Events | Edge Redis |
| Organization | Cloud PostgreSQL |
| License | Cloud PostgreSQL |
| User | Cloud PostgreSQL |
| Device | Cloud PostgreSQL |
| Training History | Cloud PostgreSQL |
| Final Scores | Cloud PostgreSQL |
| Complication History | Cloud PostgreSQL |
| Performance Profile | Cloud PostgreSQL |
| AI metadata | Cloud PostgreSQL |
| Audit logs | Cloud PostgreSQL |
| Büyük dataset | Object Storage |
| Model artifact | Object Storage |
| Büyük telemetry | Object Storage / Telemetry Storage |

---

# 45. Önerilen Nihai Mimari

```text
                    ARQENION PLATFORM

┌──────────────────────────────────────────────┐
│ VR HEADSET                                   │
│ Unreal Engine / OpenXR                       │
└────────────────────┬─────────────────────────┘
                     │
                     │ JSON Events
                     ▼
┌──────────────────────────────────────────────┐
│ ARQENION EDGE BOX                            │
│ Raspberry Pi 5                               │
│                                              │
│ Docker                                       │
│ ├── Edge API                                 │
│ ├── Procedure Engine                         │
│ ├── Scoring Engine                           │
│ ├── Complication Engine                      │
│ ├── Sync Worker                              │
│ └── Redis                                    │
└────────────────────┬─────────────────────────┘
                     │
                     │ Secure Cloud API
                     ▼
┌──────────────────────────────────────────────┐
│ ARQENION CLOUD                               │
│                                              │
│ Backend                                      │
│ ├── Organization                             │
│ ├── User                                     │
│ ├── License                                  │
│ ├── Device                                   │
│ ├── Training                                 │
│ ├── Analytics                                │
│ ├── Digital Trainee                          │
│ ├── Adaptive Training                        │
│ └── AI Gateway                               │
│                                              │
│ PostgreSQL                                   │
│ Object Storage                               │
│ Cloud AI / LLM                               │
└──────────────────────────────────────────────┘
```

---

# 46. Mimari Özeti

Arqenion mimarisi üç ana katmana ayrılır:

### VR Layer

Kullanıcı ile etkileşim ve simülasyon.

### Edge Layer

Gerçek zamanlı karar, prosedür kontrolü, scoring, komplikasyon yönetimi, cache ve offline çalışma.

### Cloud Layer

Kalıcı veri, şirket ve lisans yönetimi, kullanıcı yönetimi, raporlama, AI, dijital kursiyer ve adaptif eğitim.

Bu ayrım sayesinde VR eğitim döngüsü Cloud veya AI gecikmelerinden bağımsız çalışabilirken, merkezi veri ve AI kabiliyetleri Cloud üzerinde ölçeklenebilir biçimde yönetilebilir.
