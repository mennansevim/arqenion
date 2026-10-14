# Arqenion AI Gateway

Motor bir hata bulduğunda o hatayı kursiyere **kaynak metne dayanarak anlatan** servis. Karar vermez: doğru/yanlış, sonraki adım ve beklenen alet motordan gelir.

```
Bul → Yaz → Kontrol ─ geçti ──→ Gönder
                    └ geçmedi → Hazır klinik metin → Gönder
```

| Adım | Ne yapar | Dosya |
|---|---|---|
| Bul | Adım ve senaryo numarasıyla doğru metni bulur. Senaryo numarası yoksa yanlış verilen aletten senaryoyu bulur. Vektör araması gerekmez. | `app/knowledge.py` |
| Yaz | Yapay zekâ (OpenAI) yalnızca bu metinlerden kısa bir açıklama yazar. | `app/writer.py` |
| Kontrol | Düz kod: alanlar dolu mu, kaynak gerçekten bulunanlardan mı, önerilen alet katalogda ve bu adımda bekleniyor mu, yanlış alet doğru diye önerilmiş mi? | `app/checks.py` |
| Hazır metin | Yapay zekâ yoksa, 6 sn'de cevap vermediyse ya da kontrolden geçmediyse klinik ekibin onayladığı senaryo metni gider. | `app/fallback.py` |
| Gönder | Backend'deki `AiFeedbackDto` alanlarıyla cevap. `source`: `llm` ya da `table`. | `app/graph.py` |

Veriyi `../procedure-model/dist/` klasöründen okur (v2.4: 60 adım, 278 aktif senaryo). Beklenen aletleri `../procedure-model/tools/flow_engine.py` verir.

## Kurulum (bir kez)

```bash
cd ai-gateway
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env          # yapay zekâ için OPENAI_API_KEY'i buraya yazın (zorunlu değil)
```

Python 3.10 ve üstü gerekir.

## Çalıştırma

```bash
python cli.py --step 52 --expected SUTURE_POLYGLACTIN_0 --actual SUTURE_POLYPROPYLENE_0   # tek sapma dene
python cli.py --graph                                                                   # akış şeması (Mermaid)
uvicorn app.api:app --port 8090                                                         # web servisi
```

Web servisi açıkken `http://localhost:8090/docs` adresinden `POST /feedback` denenebilir.

İstek (backend'in göndereceği):

```json
{ "sessionId": "...", "eventId": "...", "stepId": 52, "deviationType": "WRONG_INSTRUMENT",
  "expected": "SUTURE_POLYGLACTIN_0", "actual": "SUTURE_POLYPROPYLENE_0", "severity": "MEDIUM",
  "parameters": { "SKIN_CLOSURE_TYPE": "STANDARD" } }
```

Cevap (`AiFeedbackDto` + izlenebilirlik):

```json
{ "stepId": 52, "deviationType": "WRONG_INSTRUMENT",
  "possibleRisk": "...", "explanation": "...", "recommendedAction": "...",
  "severity": "MEDIUM", "source": "llm", "modelName": "gpt-4o-mini",
  "scenarioId": "S52-E6", "sources": ["open-appendectomy@2.4.0:error:S52-E6"],
  "checks": [], "procedureVersion": "2.4.0", "durationMs": 1240 }
```

## Test

```bash
python -m pytest -q                 # 17 test; gerçek yapay zekâyı hiç çağırmaz
python tests/run_eval.py            # 278 senaryonun hepsi + senaryo eşleştirme; .env'deki yazıcıyla
python tests/run_eval.py --fake good   # sahte "iyi" yazıcıyla akışın tamamı
python tests/run_eval.py --limit 40    # gerçek yapay zekâyla ilk 40 senaryo (maliyeti sınırlar)
```

Gerçek yapay zekâ için hedef: en az %95'i kontrolden geçsin, yanlış alet öneren hiç olmasın.

## Kayıt

Her istek `logs/feedback-YYYY-MM-DD.jsonl` dosyasına bir satır olarak yazılır: girdi, cevap, kontrol sonuçları ve süreler. `logs/` repoya gitmez.

## Sıradaki iş

Backend'deki `AiFeedbackService` bu servisin `POST /feedback` adresini çağıracak. Servis kapalıysa backend bugünkü gibi kendi klinik tablosunu kullanmaya devam eder.
