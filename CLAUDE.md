# Arqenion — Claude için proje notları

Açık apendektomide ameliyathane teknikeri (scrub) eğitimi için VR simülasyonu. Kullanıcı: Mennan Sevim. Klinik eğitmen: Fatih Candemir. Yazışma dili Türkçe; kısa ve basit anlat, kodu sen yaz, adımları sade söyle.

## Temel ilke

**Kararı motor verir, AI anlatır.** Doğru/yanlış, sonraki adım ve beklenen alet deterministik motordan gelir (100 ms altı). AI yalnızca motor bir hata bulduğunda o hatayı kaynak metne dayanarak açıklar ve hiçbir zaman karar vermez. Ayrıntılar: `docs/Arqenion.TechnicalArchitecture.md`.

## Klasörler

| Klasör | Ne | Durum |
|---|---|---|
| `backend/` | .NET 8 API, motor, puanlama, SignalR (`ScoreUpdate`, `AiFeedback`) | 60 adımlık modelle çalışıyor (`Procedure:Model` = "v2.4"); eski 25 adımlı model ayarla seçilebilir |
| `backend.Tests/` | xUnit: golden, rastgele eşdeğerlik, fuzz/soak, onaylı senaryo kararları, süre | 978 test geçiyor |
| `simulator/` | Unity yerine geçen web simülatörü | Çalışıyor |
| `procedure-model/` | 60 adımlık akış modeli v2.4 ve onu üreten araçlar | Bitti, açık soru yok |
| `ai-gateway/` | Python + LangGraph AI servisi: Bul → Yaz → Kontrol → Gönder, kontrolden geçmezse hazır klinik metin | 1–6. adımlar bitti, eski modelden çeviri var, 69 test geçiyor |
| `rag/` | Eski paketler ve klinik yazışma dosyaları (zip'ler) | Arşiv |

## procedure-model (akış modeli)

- **Kaynaklar:** `arqenion.excalidraw` (birincil) ve `docs/Apendektomi ameliyatı.docx.pdf` (60 adım).
- **Yeniden üretim:** `./procedure-model/build.sh` (gerekir: `pdftotext`, `brew install poppler`). Bu komut doğrulamayı, RAG parçalarını, Mermaid çizimini ve golden dosyalarını da üretir. Derleme tekrarlanabilir: değişiklik yoksa yalnızca `createdAt` tarihi değişir.
- **Çıktılar (`dist/`):**
  - `open-appendectomy-v2.4.json`: ana model (60 adım, 22 faz, 2 alt akış, 282 senaryo, bunlardan 278'i aktif).
  - `.graph.json`: motorun okuduğu dosya.
  - `.chunks.jsonl`: AI'ın okuduğu 482 parça.
  - `.full.mmd`: Mermaid çizimi.
  - `.golden.json` (14 seçili vaka) ve `.golden-random.json` (400 rastgele vaka, sabit tohum): Python motorunun sonuçları; backend golden testleri bunlarla karşılaştırır. `tools/make_golden.py` üretir.
- **Klinik kararlar** `tools/clinical_review.py` içinde 5 tur hâlinde ve soru numaralarıyla duruyor.
  - 5. turdaki Q-B1, Q-B2, Q-B4, Q-B5 ve Q-D1 proje kararıdır, klinik teyitleri yok (`clinicallyConfirmed: false`). Pilot öncesinde Fatih'e onaylatılacak.
  - Q-B3 Fatih'in onayına dayanıyor.
- **Karar noktaları:**

  | Adım | Koşul | Dal |
  |---|---|---|
  | 11 | EXPOSURE_INSUFFICIENT | → 12 |
  | 19 | ADHESIONS_PRESENT | → 20–21 |
  | 29 | LIGATURE_NOT_SECURE | → 28 (en fazla 1 kez) |
  | 40 | BLEEDING_DETECTED | → 41 |
  | 43 | BLEEDING_AFTER_IRRIGATION | → 43a → 42 (en fazla 1 kez) |
  | 47 | COUNT_MISMATCH | → 47a–47f alt akışı |

- **Hasta parametreleri:** `PATIENT_BODY_TYPE`, `MESO_VESSELS`, `SKIN_CLOSURE_TYPE`.

## backend (60 adımlık motor)

- **Dosyalar:** `Services/FlowModelStore.cs` (graph.json + ana model), `FlowEngine.cs` (yol bulma; Python `walk()` ile aynı kurallar), `FlowProcedureService.cs` (olay doğrulama, puan, dal), `FlowClinicalText.cs` (deterministik klinik metin), `Procedures/scoring.json`, `Procedures/instrument-descriptors.json` (eşleştirme niteleyicileri: Ochsner-Kocher → "dişli" gibi).
- **Model seçimi:** `appsettings.json` → `Procedure:Model` ("v2.4" ya da "v1"). Her seans başladığı modelle sürer (`SessionRecord.ProcedureModel`). Postgres'e kolonlar veriye dokunmadan `ADD COLUMN IF NOT EXISTS` ile eklenir.
- **Adım türleri:** alet teslimi (`instrument_delivered`), onay (`step_confirmed`, cerrahın yaptığı adımlar; alet uzatmak hata), sayım (`count_confirmed`, 44–46 her biri tek kategori).
- **Senaryo alanları (`scenarios.json`):** `conditions` (dal koşulları, liste = ardışık değerlendirmeler), `parameters`, `counts`, `missingItem`, `surgeonRequestMode` ("random"/"off"), `surgeonRequests` (düğüm → istenen alet).
- **Cerrah istemi (hekim tercihi):** yalnızca modelde açıkça yazılı `SURGEON` varyantlarından gelir (`clinical_review.py` 7. tur, `variants[].instruments` = seçenek başına eşdeğer alet listesi). İlke (cerrah tercih kartı + kapalı döngü iletişim): seçenekler alet tipi, boyu ya da iplik numarası bakımından farklıysa cerrah ister, scrub yalnızca isteneni uzatır; eşdeğer aletler (ör. Stille/Semken) tek seçenektir. İstem olan adımlar: 1, 4, 8, 12, 21, 41, 48, 49, 50, 51, 56, 59. Senaryo `surgeonRequestMode: "off"` ile kapatabilir.
- **Yanıltıcı aletler:** model kataloğunda (`clinical_review.py` 6. tur, `"distractor": true`); tepside durur, hiçbir adımda kabul edilmez (No:11/12 bistüri, dişli penset, Mayo, Overholt, 2/0 Prolen, 3/0 Vicryl, klorheksidin).
- **Test:** `dotnet test backend.Tests`. Uzun soak: `ARQ_SOAK=5000 dotnet test backend.Tests --filter Soak`.
- **Yük testi:** `node scripts/load-test.mjs --url http://localhost:5080 --sessions 300 --concurrency 50`. 20 kursiyerde olay p95 ≈ 20 ms. Karar süresi p99 < 0,2 ms. Bellek içi veritabanı büyüdükçe (on binlerce olay) yavaşlar; Postgres ile ayrıca ölçülmeli.
- **Adım 4 (Q-B2) düzeltildi (2026-10-14):** kural Roux'u da kapsıyor; kilolu hastada yalnız uzun Farabeuf/Roux, zayıf/normal hastada yalnız 15 cm Farabeuf ve 14/15 cm Roux. `validate.py` bunu denetliyor.
- **Akış şeması:** simülatörde "⑂ Akış" düğmesi; `GET /api/sessions/{id}/flow` (yürünen yol, koşul planı ve değerlendirme sayıları, cerrah istemleri) ve `/api/procedure` içindeki `graph`. Beş senaryonun simülatörde yürünen yolu Python motoruyla birebir aynı (2026-10-14).

## ai-gateway (AI servisi)

- **Kurulum:**
  ```bash
  cd ai-gateway && python3 -m venv .venv && source .venv/bin/activate && pip install -r requirements.txt
  ```
- **Çalıştırma:** `python cli.py --step 52 --expected SUTURE_POLYGLACTIN_0 --actual SUTURE_POLYPROPYLENE_0`. Web servisi için `uvicorn app.api:app --port 8090` (`POST /feedback`, `GET /health`).
- **Test:** `python -m pytest -q` (69 test). `python tests/run_eval.py` 278 senaryonun hepsini ve senaryo eşleştirmeyi dener; `--fake good` ile sahte yazıcı, `--limit 40` ile ilk 40 senaryo.
- **Cevap biçimi:** backend'deki `AiFeedbackDto` alanları (`possibleRisk`, `explanation`, `recommendedAction`, `severity`, `source`), ayrıca `scenarioId`, `sources`, `checks`, `procedureVersion` ve `durationMs`.
  - `source: "llm"`: yapay zekâ yazdı ve kontrolden geçti.
  - `source: "table"`: hazır klinik metin.
- **Yapay zekâ ayarları:**
  - OpenAI, `.env` içindeki `OPENAI_API_KEY` ile çalışır. Anahtar yoksa her cevap hazır metinden gelir.
  - Varsayılan model `gpt-4o-mini`, yapay zekâ zaman aşımı 6 sn (`AI_TIMEOUT_SEC`). 3 sn'de ölçüm %82,5'te kaldı (hepsi zaman aşımı), 6 sn'de %97,5 oldu.
- **Sürümler:** langgraph 1.2.15, fastapi 0.143.0, openai 3.29.0, Python 3.10 ve üstü.
- **Eski modelden çeviri:** Backend `procedureModel: "v1"` ve açık komplikasyonu (`complication`) gönderir. `app/legacy.py`, `app/legacy_v1_map.json` tablosuyla adım numarasını ve alet kodlarını v2.4'e çevirir.
  - Komplikasyonun hikâyesi ve çözüm sırası AI'a kaynak olarak verilir.
  - Eski motorun beklediği alet v2.4'te o adımda yoksa (çelişki) AI yazmaz, boş risk döner ve backend kendi klinik tablosunu kullanır. Bilinen çelişkiler: eski 3. adım (kas diseksiyonu aleti), 21. adım (kas sütürü), 22. adım (fasya sütürü).
  - Cevapta `stepId` eski numaradır, `mappedStepId` ise v2.4'teki karşılığıdır. 60 adımlık seanslar `procedureModel: "v2.4"` ve ana adım numarasını (47b → 47) gönderir; çeviri atlanır.
- **Senaryo bulma:** Backend senaryo numarası göndermezse senaryo, yanlış verilen aletten bulunur. Senaryo başlıkları "A yerine B" ya da "A istendiği hâlde B" kalıbında. Kurallar (backend `FlowClinicalText` ile aynı): yalnız seçim senaryoları ("yerine", "istendiği hâlde", "gerekmediği hâlde", "farklı"); verilen alet adı ya da türü başlığın sağında geçmeli; başlıktaki numara verilenle uyuşmalı; başlık başka bir aleti adıyla anıyorsa ceza. Test vakaları `tests/matching_cases.json` içinde.

## Sıradaki işler

1. ~~**Backend'i bağla.**~~ Bitti. `AiFeedbackService` gateway'in `POST /feedback` adresini çağırıyor (`AiGateway:BaseUrl`, `AiGateway:TimeoutMs` = 8000). Gateway kapalıysa, hata verirse ya da süre dolarsa `clinical-knowledge.json` tablosu kullanılır. Bilinen açık: senaryo eşleşmeyen sapmalarda (ör. `WRONG_ORDER`) gateway `possibleRisk`'i boş döndürüyor, backend bu durumda kendi tablosuna düşüyor.
2. ~~**Gerçek yapay zekâyla ölç.**~~ Bitti. `run_eval.py --limit 40`, 6 sn sınırla: %97,5 kontrolden geçti, yanlış alet öneren 0, senaryo eşleştirme 6/6. Tüm 278 senaryo: %97,1 (270/278), yanlış alet 0; 8 hata "katalogda olmayan alet" (S33-E1..E5: `PEANUT_SPONG` yazım hatası; S39-E3, S45-E3, S60-E3: aletsiz adımlarda faz kodu `PHxx` alet diye yazılmış), hepsi güvenli şekilde hazır metne düştü. Düzeltme: `writer.py` yapılandırılmış çıktı şeması (alet ve kaynak kodu yalnızca verilen listeden; aletsiz adımda alet alanı yok). Sonrasında tüm 278 senaryo: %100 (278/278), yanlış alet 0, ortanca 2,1 sn, en uzun 3,7 sn.
3. ~~**Backend'i 60 adıma geçir.**~~ Bitti (2026-10-14). Golden (14 + 400 rastgele vaka), fuzz/soak (5000 seans), 62 onaylı senaryo karar tablosu ve HTTP yük testi geçiyor; simülatör 60 adımı ve dalları oynatıyor. Ameliyat öncesi bölümler (PRE01–09) henüz oynanmıyor.
4. **VR olay sözleşmesi:** `docs/vr-events.md`.
5. **Adaptif motor.**
6. **Fatih ile doğrulama ve pilot.**

Yol haritası dokümanı: https://claude.ai/code/artifact/23893c77-41ab-4a96-840f-e86dbc3bac10
Mimari sunumu: https://claude.ai/artifact/321t39utTwensX2wyiWzrv

## Kurallar

- API anahtarını hiçbir zaman commit etme (`.env` repoya gitmez).
- `backend/Procedures/open-appendectomy-v1.json` dosyasını değiştirme ya da silme; geçiş bir ayarla yapılacak.
- Kullanıcının commit edilmemiş başka değişiklikleri var. Commit yalnızca kullanıcı isterse yapılır ve yalnızca ilgili klasörler commit edilir.
- `ai-gateway/.venv-vm` önceki bir oturumda oluşturulmuş bir Linux test ortamıdır, git onu yok sayar. Mac'te `.venv` kullanılır.
