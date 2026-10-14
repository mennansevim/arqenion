# Açık Apendektomi — RAG + Koşullu Akış Paketi (v2.4.0)

v2, klinik ekibin 30 soruya verdiği cevapları (akis_sorulari.md, 2026-10-07) modele işler. Kaynaklar arasındaki 21 farkın hepsi kapandı. Klinik kararlar `clinical_review.py` içinde soru numarasıyla tutulur; Excalidraw yeniden derlendiğinde kaybolmaz.

## Klasör yapısı

```
procedure-model/
  build.sh            # tek komutla yeniden üretim (kaynak → model → doğrulama → RAG → Mermaid)
  ROADMAP.md          # sıradaki işler
  dist/               # üretilen çıktılar (commit edilir; backend ve AI Gateway buradan okur)
    open-appendectomy-v2.4.json          # master model: 60 adım, 2 alt akış, 9 ameliyat öncesi bölüm, 282 senaryo
    open-appendectomy-v2.4.chunks.jsonl  # vektör DB'ye yüklenecek 474 parça (embed edilen tek dosya)
    open-appendectomy-v2.4.graph.json    # akış motoru için kompakt graf
    open-appendectomy-v2.4.full.mmd      # tüm dallar + alt akışlar, Mermaid
  tools/
    model_data.py       # elle kodlanmış katalog + 60 adım
    clinical_review.py  # klinik kararlar (5 tur), model_data'nın üstüne yazar; her karar soru numarasıyla
    build.py, parse_excalidraw.py         # Excalidraw + PDF → model
    validate.py, rag_export.py            # doğrulama; chunks + graph üretimi
    flow_engine.py                        # koşul + hasta parametresi → yol, beklenen aletler, Mermaid, LLM araç tanımı
    parse_suture_table.py, ref/suture_reference.json   # iplik tablosu → JSON
    make_review_doc.py, make_review_xlsx.py, import_review_xlsx.py   # klinik onay belgesi/Excel'i
  review/             # klinik ekiple yazışma: açık sorular, onay Excel'i
```

Kaynaklar repoda zaten var: `arqenion.excalidraw` (birincil) ve `docs/Apendektomi ameliyatı.docx.pdf` (ikincil).

**Backend ile ilişkisi:** `backend/Procedures/open-appendectomy-v1.json` (25 adım, iç sürüm 3.0) hâlâ çalışan backend'in dosyası; bu klasör ona dokunmaz. Geçiş planı `ROADMAP.md` Faz 1'de.

## v2.3 → v2.4 (kalan sorular kapatıldı, 2026-10-09)

Açık soru kalmadı (`openQuestions: []`). Kararlar `resolvedQuestions` alanında gerekçesiyle duruyor. Her kararda `clinicallyConfirmed` alanı var: Q-B3 Fatih'in onayına dayanıyor, diğerleri proje kararı (Mennan Sevim).

| Soru | Adım | Karar |
|---|---|---|
| Q-B1 | 8 | Penset (Stille/DeBakey) ve 2 adet eğri Crile ikisi de kabul; hekim tercihi |
| Q-B2 | 4 | Kilolu hastada Adım 4'te de 15 cm'den uzun Farabeuf, gerekirse uzun Roux (`parameterRules` eklendi) |
| Q-B3 | 52 | Fasya 0 numara Vicryl (onaylı S52-E6 başlığından) |
| Q-B4 | 29 | Yeniden bağlama en fazla 1 kez |
| Q-B5 | 50 | "1/0" = 1 numara; kabul edilen Vicryl 0 ve 1 |
| Q-D1 | 56 | Standart kapatmada 3/0 Prolen, 4/0 Prolen ve 4/0 Polyamid üçü de kabul; hekim tercihi |

- Hiçbir adımda `pendingClinicalConfirmation` kalmadı; RAG metinlerinde "teyit bekliyor" ifadesi yok.
- Doğrulama yeni kontrolleri içeriyor: açık soru/teyit tutarlılığı ve Adım 4 kilolu hasta kuralı.

## v2.2 → v2.3 (taslak onayı, Fatih Candemir, 2026-10-09)

Kaynak: `hata-senaryolari-onay-v2.2.xlsx` (3. dönüş). 6 taslağın 5'i onaylandı, 1'i düzeltilerek onaylandı. Hepsi `ACTIVE` oldu ve yeni kimlik aldı; eski kimlik `review.formerId` alanında duruyor.

| Taslak | Yeni kimlik | Karar |
|---|---|---|
| S50-D1 | S50-E6 | Onay (başlık Fatih'in düzeltmesiyle) |
| S50-D2 | S50-E7 | Onay |
| S52-D1 | S52-E6 | Onay (komplikasyon listesi Fatih'in düzeltmesiyle) |
| S52-D2 | S52-E7 | Onay |
| S56-D1 | S56-E5 | Onay |
| S56-D2 | S56-E6 | Düzeltme: standart kapatma 4/0 Polyamid |

- **S50-E2** geçersiz (`SUPERSEDED`); yerini S50-E6 aldı.
- **Adım 56:** 4/0 Polyamid standart kapatmaya geri döndü. Standart = 3/0 Prolen, 4/0 Prolen veya 4/0 Polyamid. 7 Ekim mesajı ile Excel çelişiyor → **Q-D1** açıldı.
- **Q-C1** (taslak onayı) kapandı.
- **Senaryo durumları:** ACTIVE 255 · CONDITIONAL 12 · ADAPTED 11 · SUPERSEDED 4 · DRAFT 0.
- **Açık sorular:** Q-B1…Q-B5, Q-D1.

## v2.1 → v2.2 (iplik tablosu + yeni senaryo taslakları)

- **İplik referans kataloğu:** `catalogs.sutureReference` alanında 49 iplik (`ref/suture_reference.json`, kaynak: klinik ekibin tablosu). Her iplik için materyal, emilebilirlik, tek telli/örgülü yapı ve emilme süresi var.
- **Modeldeki iplikler tabloya bağlandı:** Her sütür kodunda `referenceName`, `absorptionDays` ve `structure` alanları dolu. Örnekler:
  - Vicryl: 56–70 gün
  - Monocryl: 91–119 gün
  - Prolen, ipek ve poliamid: emilmez
- **Yeni taslak hata senaryoları (6 adet, `reviewStatus=DRAFT`, RAG'a girmez):**
  - Adım 50: S50-D1 (emilmeyen Prolen; S50-E2'nin yerine), S50-D2 (Vicryl Rapide karışıklığı)
  - Adım 52: S52-D1 (Prolen; S52-E1'in yerine), S52-D2 (Vicryl Rapide karışıklığı)
  - Adım 56: S56-D1 (estetik kapatmada Prolen; S56-E1'in yerine), S56-D2 (standart kapatmada Monocryl)

  İnceleme belgesi `hata-senaryolari-inceleme-v2.2.md`, modelden `make_review_doc.py` ile üretilir.
- **RAG:** 469 parça. 49'u yeni `suture_reference` parçası; "Vicryl kaç günde emilir?" gibi soruları cevaplar.

## v2.0 → v2.1 (2. tur, Fatih Candemir)

- **Adım 8:** Peritonu tutmak için Stille/DeBakey penset ya da 2 adet eğri Crile klemp; hekim tercihi, teyit bekliyor (Q-B1). Excalidraw'daki Crile senaryoları tekrar aktif (koşullu).
- **Kilolu hasta:** Adım 11'de 15 cm'den uzun Farabeuf, Adım 12'de gerekirse 15 cm'den uzun Roux kullanılır. Adım 13'te yeni alet verilmez. PDF'teki Langenbeck/Richardson kuralı kaldırıldı.
- **Sayım (47f):** Eksik malzeme tekrar sayımda, atıklarda ve görüntülemede bulunamazsa paketten eksik çıktığı kabul edilir.
- **Döngü sınırları modelde (`maxIterations`):**
  - yıkama sonrası ara hemostaz en fazla 1 kez (kanama kontrolü toplam 2 kez),
  - yeniden bağlama en fazla 1 kez (Q-B4),
  - sayım alt akışı en fazla 1 kez.

  Kural R09: her döngünün klinik ekipçe belirlenmiş açık bir üst sınırı vardır.
- **Adım 50:** Yalnızca Vicryl 0 veya 1. Prolen ve 2/0 kullanılmaz. S50-E2 ve S50-E3 yeniden aktif, S50-E1 geçersiz.
- **Adım 52:** Fasya 0 Vicryl ile kapatılır (Prolen yerine; numara Q-B3 ile teyit bekliyor). Senaryo metinleri uyarlandı (`ADAPTED`).
- **Adım 56:** Kapatma tipine göre sütür (`SKIN_CLOSURE_TYPE`): estetik → 4/0 Monocryl, standart → 3/0 veya 4/0 Prolen. 4/0 Polyamid akıştan çıktı.
- **İplik kataloğu:** `material`, `absorbable` ve boş `absorptionDays` alanları var; klinik ekibin tablosu gelince doldurulacak.
- **Senaryo durumları:** ACTIVE 250 · CONDITIONAL 12 · ADAPTED 11 · SUPERSEDED 3.
  - `ADAPTED`: senaryo metni klinik karara göre otomatik düzeltildi; RAG'a girer.
  - `SUPERSEDED`: RAG'a girmez.

## v1 → v2 değişiklikleri

**Alet kararları**
- **Adım 1:** 10 ve 15 numara bistüri ucu ikisi de kabul edilir (hekim tercihi).
- **Adım 8:** Periton Stille veya DeBakey penseti ile tutulur. Excalidraw'daki 6 Crile senaryosu geçersiz sayıldı.
- **Adım 9:** Bistüri ucu değiştirilir. Değiştirilmezse enfeksiyonun gelişip gelişmeyeceğini adaptif motor seçer.
- **Adım 13:** Vücut tipine göre ekartör kuralı eklendi (`PATIENT_BODY_TYPE`).
- **Adım 21:** Stille penset eklendi.
- **Adım 23:** Klemp adedi damar sayısına, klemp tipi damar çapına göre belirlenir (`MESO_VESSELS`):
  - küçük damar → mosquito,
  - orta damar → Crile,
  - büyük damar → pean.
- **Adım 30:** İşaret pensi Crile klemptir; Babcock işaret için kullanılmaz.
- **Adım 38:** Babcock appendiksle birlikte patoloji kabına bırakılır. Sirküle onu alıp kirli alanda tutar; alet sayımda kalır.
- **Adım 48:** Crile eğri ×2 ya da Stille penset (hekim tercihi).
- **Adım 50:** Emilebilir sütür zorunlu; kalınlık 1, 0 veya 2/0. Prolen'in durumu teyit bekliyor (Q-A1).
- **Katalog:** Metzenbaum makas sete ekleniyor. Sütürler ve serum fizyolojik haricen açılan sarf malzemesi. Sütürlerde materyal ve emilebilirlik bilgisi var.

**Akış**
- **Sayım uyuşmazlığı alt akışı (47a–47f):** 47→44 döngüsünün yerini aldı.
  1. Önce eksik kalem tekrar sayılır.
  2. Bulunamazsa saha araması yapılır.
  3. Sahada da yoksa radyolojik görüntüleme yapılır.
  4. Görüntülemede bulunursa malzeme çıkarılır ve peritondan itibaren yeniden kapatılır.
- **Yıkama sonrası kanama ara adımı (43a):** Kanama monopolar koter ile durdurulur, Adım 40'ın hazırlıkları tekrarlanır ve yeniden yıkamaya (Adım 42) dönülür. Bu döngü v2.1'de en fazla 1 kez çalışır.
- **Zorunluluk teyidi:** Güdük gömme (27, 35–37) ve yıkama (42, 43, 53) her vakada yapılır.

## Koşul kaynakları

Her koşulun bir `source` alanı var:

| source | Kim belirler | Örnek |
|---|---|---|
| `SCENARIO` | Senaryo, seans başında | Yapışıklık (scenarios.json `hasAdhesion`), yetersiz görüş |
| `ADAPTIVE` | Adaptive Training Engine, kursiyerin dijital ikizine göre; AI öneri verebilir | Kanama, bağın güvensiz olması, eksik malzemenin yeri, enfeksiyon |
| `RUNTIME` | Kursiyerin olayları | Sayım uyuşmazlığı |

Klinik ekip kanama ve enfeksiyon gibi olayların gerçekleşip gerçekleşmeyeceğinin AI'a bırakılmasını istedi. Bu, "doğru/yanlış kararını motor verir" ilkesini bozmuyor:

- Seçim yalnızca izin verilen değerler (true/false) arasından yapılır.
- Seçim motora girdi olarak verilir; yolu yine motor yürür.
- Her seçim kayda geçer.
- Sınav modunda bu koşullar senaryoda sabitlenir.

`walk()` sonucundaki `adaptiveDecisions`, yolda karar bekleyen adaptif koşulları listeler. Adaptive Engine'in neye karar vermesi gerektiği buradan okunur.

## Akış motoru

```python
from flow_engine import FlowEngine
fe = FlowEngine("procedure-model/dist/open-appendectomy-v2.4.graph.json")
r = fe.walk(
    {"ADHESIONS_PRESENT": True,
     "BLEEDING_AFTER_IRRIGATION": [True, False],   # 1. yıkamada kanama var, 2.'de yok
     "COUNT_MISMATCH": True, "ITEM_FOUND_IN_FIELD": True},
    parameters={"PATIENT_BODY_TYPE": "OBESE", "SKIN_CLOSURE_TYPE": "AESTHETIC",
                "MESO_VESSELS": [{"caliber": "SMALL"}, {"caliber": "LARGE"}]})
r["path"]                    # [..., 42, 43, '43a', 42, 43, 44, ..., 47, '47a', '47b', '47c', 47, 48, ...]  (döngüler maxIterations ile sınırlı)
r["expectedInstruments"]["23"]   # klempler: mosquito x1, pean x1
r["adaptiveDecisions"]       # karar verilmemiş adaptif koşullar
print(fe.mermaid(r, 40, 49))
```

CLI kullanımı:

```bash
python3 procedure-model/tools/flow_engine.py --cond BLEEDING_AFTER_IRRIGATION=true,false --param PATIENT_BODY_TYPE=OBESE --from 38 --to 50
python3 procedure-model/tools/flow_engine.py --full
```

LLM araç tanımı `fe.TOOL_SCHEMA` içinde; koşul listesi graph.json'dan otomatik üretiliyor.

## RAG notları

- `SUPERSEDED` (4) ve `DRAFT` senaryolar RAG'a girmez.
- `CONDITIONAL` (12) ve `ADAPTED` (11) senaryolar girer. Durumları metinde ve `metadata.reviewStatus` alanında yazar.
- Alt akış adımları `chunkType=subflow_step` ile girer. `metadata.step` alanı ana adım numarasıdır (47a → 47).

## Yeniden üretim

```bash
./procedure-model/build.sh        # gerekenler: python3 3.10+, pdftotext (macOS: brew install poppler)
```

Klinik bir kararı değiştirmek için `tools/clinical_review.py` içinde ilgili tura bakın, sonra `build.sh` çalıştırın. Doğrulama hata verirse çıktı üretilmez.

## Uyarı

Bu içerik denetimli klinik simülasyon ve eğitim içindir. v2.4'teki proje kararları (`clinicallyConfirmed: false`) klinik ekip uygun gördüğünde gözden geçirilebilir; değiştirmek için `clinical_review.py` içindeki 5. tura bakın.
