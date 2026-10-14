# -*- coding: utf-8 -*-
"""Klinik teyit (akis_sorulari.md, 2026-10-07) kararlarının modele uygulanması.

model_data.py kaynaklardan (PDF + Excalidraw) çıkan ham kodlamayı tutar; bu dosya
klinik ekibin cevaplarını üstüne yazar. Her değişiklik `clinicalReview` kaydıyla
hangi soruya dayandığını taşır, böylece Excalidraw yeniden derlendiğinde kararlar kaybolmaz.
"""
from model_data import *  # noqa: F401,F403  (STEPS, INSTRUMENTS, CONDITIONS, BRANCHES, NEXT_OVERRIDE, I, PREOP içermez)

VERSION = "2.0.0"
FILE_NAME = "open-appendectomy-v2.json"
REVIEW_SOURCE = {"file": "akis_sorulari.md", "receivedAt": "2026-10-07", "reviewer": "Klinik ekip (ameliyathane eğitmeni)"}


def review(n, q, decision, note=None):
    STEPS[n].setdefault("review", []).append({"question": q, "decision": decision, **({"note": note} if note else {})})


def resolve_conflicts(n, q, text):
    for c in STEPS[n].get("conflicts", []):
        c["status"] = "RESOLVED_BY_CLINICAL_REVIEW"
        c["reviewQuestion"] = q
        c["resolution"] = text


def drop(n, code):
    STEPS[n]["inst"] = [i for i in STEPS[n]["inst"] if i["code"] != code]


# ---------------------------------------------------------------- katalog
INSTRUMENTS.update({
    "FORCEPS_DEBAKEY": {"name": "DeBakey diseksiyon penseti", "category": "forceps"},
    "CLAMP_MOSQUITO": {"name": "Mosquito klemp", "category": "clamp", "vesselCaliber": "SMALL"},
    "CLAMP_PEAN": {"name": "Pean klemp", "category": "clamp", "vesselCaliber": "LARGE"},
    "RETRACTOR_LANGENBECK": {"name": "Langenbeck ekartör", "category": "retractor"},
    "RETRACTOR_RICHARDSON": {"name": "Richardson ekartör", "category": "retractor"},
})
# S30: hemostatik klempler aynı şekil, farklı uç kalınlığı → damar çapı
INSTRUMENTS["CLAMP_CRILE_CURVED_14"]["vesselCaliber"] = "MEDIUM"
INSTRUMENTS["CLAMP_CRILE_STRAIGHT_14"]["vesselCaliber"] = "MEDIUM"
HEMOSTAT_BY_CALIBER = {"SMALL": "CLAMP_MOSQUITO", "MEDIUM": "CLAMP_CRILE_CURVED_14", "LARGE": "CLAMP_PEAN"}

# S29: Metzenbaum sete eklenecek; sütür ve serum haricen açılan sarf
for code in ("SCISSORS_METZENBAUM", "SCISSORS_METZENBAUM_CURVED"):
    INSTRUMENTS[code].pop("inApendectomySetImage", None)
    INSTRUMENTS[code].update({"inSet": True, "supply": "SET", "note": "Klinik teyit S29: apendektomi setine eklenecek"})
for code, d in INSTRUMENTS.items():
    if d["category"] in ("suture",) or code in ("ISOTONIC_SALINE",):
        d.update({"inSet": False, "supply": "CONSUMABLE"})

# Emilebilirlik (malzeme bilgisi). Prolen = polipropilen → emilmez. Bkz. açık soru Q-A1.
ABSORBABLE = {
    "SUTURE_SILK_3_0": ("İpek", False),
    "SUTURE_POLYGLACTIN_2_0": ("Poliglaktin 910", True),
    "SUTURE_POLYGLACTIN_0": ("Poliglaktin 910", True),
    "SUTURE_POLYGLACTIN_1": ("Poliglaktin 910", True),
    "SUTURE_POLYPROPYLENE_0": ("Polipropilen", False),
    "SUTURE_POLYPROPYLENE_1": ("Polipropilen", False),
    "SUTURE_POLYAMIDE_4_0": ("Poliamid", False),
}
for code, (mat, ab) in ABSORBABLE.items():
    INSTRUMENTS[code].update({"material": mat, "absorbable": ab})
INSTRUMENTS["SUTURE_POLYPROPYLENE_0"]["absorbableNote"] = INSTRUMENTS["SUTURE_POLYPROPYLENE_1"]["absorbableNote"] = \
    "Klinik cevapta 'Prolen emilebilir' denmiş; polipropilen emilmeyen bir materyaldir. Teyit bekliyor (Q-A1)."

ACTIONS.update({"SEARCH": "Arama", "IMAGING": "Radyolojik görüntüleme", "RETRIEVE": "Malzemeyi çıkarma", "RECORD": "Kayıt"})
TARGETS.update({"PATIENT": "Hasta (görüntüleme)", "MISSING_ITEM": "Eksik malzeme"})

# ---------------------------------------------------------------- koşullar ve parametreler
# source: SCENARIO = seans başında senaryo belirler · ADAPTIVE = Adaptive Training Engine
# dijital ikize göre seans içinde seçer (AI önerebilir) · RUNTIME = kursiyerin olaylarından hesaplanır
CONDITIONS.update({
    "BLEEDING_AFTER_IRRIGATION": "Yıkama ve aspirasyondan sonra sahada kanama görüldü",
    "ITEM_FOUND_ON_RECOUNT": "Eksik malzeme tekrar sayımda bulundu (sayım hatası)",
    "ITEM_FOUND_IN_FIELD": "Eksik malzeme saha aramasında bulundu",
    "ITEM_FOUND_ON_IMAGING": "Eksik malzeme radyolojik görüntülemede hastada görüldü",
    "SURGICAL_SITE_INFECTION": "Kirli bistüri ucu kullanımına bağlı cerrahi alan enfeksiyonu gelişti",
})
CONDITION_META = {
    "EXPOSURE_INSUFFICIENT": {"source": "SCENARIO", "evaluatedAt": 11},
    "ADHESIONS_PRESENT": {"source": "SCENARIO", "evaluatedAt": 19, "scenarioField": "patient.hasAdhesion"},
    "LIGATURE_NOT_SECURE": {"source": "ADAPTIVE", "evaluatedAt": 29},
    "BLEEDING_DETECTED": {"source": "ADAPTIVE", "evaluatedAt": 40, "reviewQuestion": 27},
    "BLEEDING_AFTER_IRRIGATION": {"source": "ADAPTIVE", "evaluatedAt": 43, "reviewQuestion": 27},
    "COUNT_MISMATCH": {"source": "RUNTIME", "evaluatedAt": 47, "note": "count_confirmed sayıları beklenenle uyuşmadığında; senaryo eksik malzeme de enjekte edebilir"},
    "ITEM_FOUND_ON_RECOUNT": {"source": "ADAPTIVE", "evaluatedAt": "47a", "reviewQuestion": 26},
    "ITEM_FOUND_IN_FIELD": {"source": "ADAPTIVE", "evaluatedAt": "47b", "reviewQuestion": 26},
    "ITEM_FOUND_ON_IMAGING": {"source": "ADAPTIVE", "evaluatedAt": "47d", "reviewQuestion": 26},
    "SURGICAL_SITE_INFECTION": {"source": "ADAPTIVE", "evaluatedAt": "debrief", "kind": "OUTCOME", "reviewQuestion": 2,
                                "note": "Akışı değiştirmez; sonucu (debrief, skor, dijital ikiz) etkiler"},
}
PARAMETERS = {
    "PATIENT_BODY_TYPE": {"type": "enum", "values": ["THIN", "NORMAL", "OBESE"], "source": "SCENARIO",
                          "scenarioField": "patient.bodyType", "default": "NORMAL", "reviewQuestion": 11},
    "MESO_VESSELS": {"type": "list", "items": {"caliber": ["SMALL", "MEDIUM", "LARGE"]}, "source": "SCENARIO",
                     "default": [{"caliber": "MEDIUM"}, {"caliber": "MEDIUM"}], "reviewQuestion": 15,
                     "note": "Klemplenecek mezappendiks damarları; adet ve çap klemp sayısını ve tipini belirler"},
}

# ---------------------------------------------------------------- adım kararları
# Q1 · Adım 38
review(38, 1, "A+", "Babcock appendiksle birlikte formaldehitli kaba bırakılır; sirküle Babcock'u alıp kirli alanda tutar. Alet odadan çıkmaz, sayımda eksik oluşmaz.")
resolve_conflicts(38, 1, "Klinik teyit (S1): PDF doğru. Babcock kaba bırakılır, sirküle alıp kirli alanda tutar; alet sayımda kalır.")
STEPS[38]["by"] = ["SURGEON", "SCRUB", "CIRCULATOR"]
STEPS[38]["inst"] = [I("CLAMP_BABCOCK_16", "IN_USE", note="Spesimenle birlikte kaba bırakılır; sirküle alıp kirli alanda tutar"),
                     I("SPECIMEN_CONTAINER_FORMALIN_10", "SUPPORT", note="Sirküle hazırlar")]
STEPS[38]["notes"] = ["Babcock ameliyat odasından çıkmaz; kapanış sayımında kirli alanda sayılır."]
STEPS[38]["instrumentDisposition"] = {"code": "CLAMP_BABCOCK_16", "location": "DIRTY_AREA", "handledBy": "CIRCULATOR", "countedAtClosure": True}

# Q2 · Adım 9
review(9, 2, "B", "Uç değiştirilir. Değiştirilmezse enfeksiyon olasılığı; enfeksiyonun gelişip gelişmeyeceği dijital ikize göre adaptif motor/AI tarafından seçilir.")
resolve_conflicts(9, 2, "Klinik teyit (S2): Excalidraw doğru, bistüri ucu değiştirilir.")
STEPS[9]["adaptiveOutcomes"] = [{"onScenario": "S09-E3", "condition": "SURGICAL_SITE_INFECTION", "decidedBy": "ADAPTIVE_ENGINE",
                                 "inputs": ["digitalTwin.level", "digitalTwin.weakAreas.sterility"],
                                 "effect": "Postoperatif enfeksiyon olarak debrief'te gösterilir; sterilite skorunu etkiler"}]
STEPS[34].setdefault("adaptiveOutcomes", []).append(
    {"onScenario": "S34-E1", "condition": "SURGICAL_SITE_INFECTION", "decidedBy": "ADAPTIVE_ENGINE",
     "inputs": ["digitalTwin.level", "digitalTwin.weakAreas.sterility"], "effect": "Mevcut backend K4 (DIRTY_BLADE_INFECTION) ile aynı sonuç ailesi"})

# Q3 · Adım 1
review(1, 3, "A ve B", "Hekimin tercihine göre 10 veya 15 numara; ikisi de doğru.")
resolve_conflicts(1, 3, "Klinik teyit (S3): 10 ve 15 numara uç ikisi de kabul; hekim tercihi.")
STEPS[1]["inst"] = [I("SCALPEL_BLADE_10", "ALTERNATIVE", note="Hekim tercihi"), I("SCALPEL_BLADE_15", "ALTERNATIVE", note="Hekim tercihi"),
                    I("SCALPEL_HANDLE_3", "SUPPORT")]
STEPS[1]["variants"].append({"id": "SKIN_BLADE", "options": ["No:10", "No:15"], "decidedBy": "SURGEON"})

# Q4 · Adım 30
review(30, 4, "B", "Crile klemp; işaret için Babcock kullanılmaz.")
resolve_conflicts(30, 4, "Klinik teyit (S4): Crile klemp düz 14 cm; Babcock işaret pensi olarak kullanılmaz.")

# Q5 · Adım 50
review(50, 5, "Genişletildi", "1, 0 veya 2(/0) kullanılabilir; 2/0'dan ince iplik kullanılmaz. İplik emilebilir olmalı (kritik).")
resolve_conflicts(50, 5, "Klinik teyit (S5): 1, 0 veya 2/0; emilebilir iplik zorunlu. Prolen'in durumu Q-A1 ile teyit bekliyor.")
STEPS[50]["name"] = "Kas tabakasının emilebilir sütürle kapatılması"
STEPS[50]["inst"] = [I("NEEDLE_HOLDER_DEBAKEY_18"),
                     I("SUTURE_POLYGLACTIN_1", "ALTERNATIVE"), I("SUTURE_POLYGLACTIN_0", "ALTERNATIVE"),
                     I("SUTURE_POLYGLACTIN_2_0", "ALTERNATIVE", note="En ince kabul edilen kalınlık"),
                     I("SUTURE_POLYPROPYLENE_1", "ALTERNATIVE", note="TEYİT BEKLİYOR: polipropilen emilmez (Q-A1)"),
                     I("SUTURE_POLYPROPYLENE_0", "ALTERNATIVE", note="TEYİT BEKLİYOR: polipropilen emilmez (Q-A1)"),
                     I("FORCEPS_SEMKEN_NON_TOOTHED_15_5", "SUPPORT")]
STEPS[50]["variants"] = [{"id": "SUTURE_SIZE", "options": ["1", "0", "2/0"], "decidedBy": "SURGEON"}]
STEPS[50]["rules"] = [{"id": "R50-1", "text": "Kas tabakasında emilebilir sütür kullanılır.", "check": "instrument.absorbable == true"},
                      {"id": "R50-2", "text": "2/0'dan daha ince sütür (3/0, 4/0 ...) kullanılmaz.", "check": "suture.size >= 2/0"}]
STEPS[50]["pendingClinicalConfirmation"] = ["Q-A1", "Q-A2"]

# Q6–Q8, Q10, Q12, Q14, Q16–Q19 · Excalidraw (B) onaylandı
for q, n in [(6, 4), (7, 6), (8, 7), (10, 10), (12, 20), (14, 22), (16, 25), (17, 28), (18, 35), (18, 37), (19, 36)]:
    review(n, q, "B")
    resolve_conflicts(n, q, f"Klinik teyit (S{q}): Excalidraw (B) doğru.")
STEPS[10]["inst"] = [i for i in STEPS[10]["inst"] if not (i["code"] == "FORCEPS_STILLE_NON_TOOTHED_15" and i["usage"] == "IN_USE")]

# Q9 · Adım 8 → PDF (A)
review(8, 9, "A", "Periton Stille veya DeBakey diseksiyon penseti ile tutulur.")
resolve_conflicts(8, 9, "Klinik teyit (S9): PDF doğru, Stille veya DeBakey diseksiyon penseti.")
STEPS[8]["inst"] = [I("FORCEPS_STILLE_NON_TOOTHED_15", "ALTERNATIVE"), I("FORCEPS_DEBAKEY", "ALTERNATIVE")]
STEPS[8]["name"] = "Peritonun penset ile tutulması"

# Q11 · Adım 13 + vücut tipi kuralı
review(13, 11, "B + vücut tipi kuralı", "Kilolu hastada daha uzun uçlu ekartör gerekir; bu davranış öğretilmeli.")
resolve_conflicts(13, 11, "Klinik teyit (S11): Excalidraw (B) + PDF'teki hasta kilosuna göre ekartör kuralı eklendi.")
STEPS[13]["parameterRules"] = [{
    "parameter": "PATIENT_BODY_TYPE",
    "expectedInstruments": {
        "THIN": ["RETRACTOR_FARABEUF_15_SET", "RETRACTOR_LANGENBECK"],
        "NORMAL": ["RETRACTOR_LANGENBECK"],
        "OBESE": ["RETRACTOR_RICHARDSON"],
    },
    "source": "PDF alet tablosu, Adım 13 + klinik teyit S11",
    "status": {"OBESE": "PENDING_CONFIRMATION (Q-A4)", "NORMAL": "PENDING_CONFIRMATION (Q-A4)"},
    "note": "Kilolu hastada daha uzun uçlu ekartör; tam alet adı teyit bekliyor.",
}]
STEPS[13]["newInstrument"] = "DEPENDS_ON_PARAMETER"

# Q13 · Adım 21 + Stille
review(21, 13, "B + Stille", "Stille penset ile doku tutulur, Metzenbaum veya monopolar ile yapışıklık ayrılır.")
resolve_conflicts(21, 13, "Klinik teyit (S13): Excalidraw (B) + Stille penset eklendi.")
STEPS[21]["inst"].insert(2, I("FORCEPS_STILLE_NON_TOOTHED_15", "SUPPORT", note="Dokuyu tutmak için"))

# Q15 · Adım 23 klemp sayısı ve tipi damara göre
review(23, 15, "B + dinamik klemp", "Klemp sayısı damar sayısına göre değişir; tip damar çapına göre (S30).")
resolve_conflicts(23, 15, "Klinik teyit (S15, S30): Excalidraw düzeni (Semken, koter, portegü) korunur; klemp adedi damar sayısı, tipi damar çapı ile belirlenir.")
STEPS[23]["inst"] = [I("CLAMP_CRILE_CURVED_14", note="Varsayılan: orta çaplı damar; adet ve tip MESO_VESSELS'e göre"),
                     I("LIGATURE_SURGEON_CHOICE", "SUPPORT"), I("FORCEPS_SEMKEN_NON_TOOTHED_15_5", "ALTERNATIVE"),
                     I("ESU_MONOPOLAR_PEN", "ALTERNATIVE"), I("NEEDLE_HOLDER_DEBAKEY_18", "ALTERNATIVE", note="İğneli ligatürde")]
STEPS[23]["dynamicQuantity"] = {"parameter": "MESO_VESSELS", "rule": "Her damar için 1 hemostatik klemp",
                                "instrumentByCaliber": HEMOSTAT_BY_CALIBER}
STEPS[24]["inst"] = [I("SCISSORS_METZENBAUM_CURVED", note="Sete eklenecek (S29)"), I("CLAMP_CRILE_CURVED_14", "IN_USE", note="Adım 23'teki klempler")]
STEPS[41]["inst"].insert(0, I("CLAMP_MOSQUITO", "ALTERNATIVE", note="Küçük damar"))
STEPS[41]["inst"].insert(1, I("CLAMP_PEAN", "ALTERNATIVE", note="Büyük damar"))
STEPS[41]["selectionRule"] = {"by": "vesselCaliber", "instrumentByCaliber": HEMOSTAT_BY_CALIBER, "reviewQuestion": 30}

# Q20 · Adım 48
review(48, 20, "B veya Stille", "2 adet Crile eğri ya da Stille penset; hekim tercihi.")
resolve_conflicts(48, 20, "Klinik teyit (S20): 2 adet Crile eğri 14 cm veya Stille penset; hekim tercihi.")
STEPS[48]["inst"] = [I("CLAMP_CRILE_CURVED_14", "ALTERNATIVE", qty=2), I("FORCEPS_STILLE_NON_TOOTHED_15", "ALTERNATIVE")]

# Q21 · Adım 51
review(51, 21, "B")
resolve_conflicts(51, 21, "Klinik teyit (S21): Excalidraw (B) doğru.")

# Q23, Q24 · zorunluluk teyidi
for n in (27, 35, 36, 37):
    review(n, 23, "Zorunlu", "Güdük her vakada gömülür; çuval ağzı gibi: önce içe çevrilir, sonra ip sıkılarak büzülür.")
STEPS[27]["notes"] = ["Klinik teyit S23: güdük gömme her vakada yapılır (zorunlu)."]
for n in (42, 43, 53):
    review(n, 24, "Zorunlu", "Yıkama her vakada yapılır.")
review(29, 25, "Doğru", "Bağ güvenli değilse Adım 28'e dönülür.")

# Q26 · sayım uyuşmazlığı alt akışı (47 → 44 döngüsü yerine)
BRANCHES[47] = [{"when": "COUNT_MISMATCH", "goTo": "47a", "note": "Sayım uyuşmazlığı alt akışı"}]
review(47, 26, "Alt akış", "Önce eksik kalem tekrar sayılır; sürerse saha araması ve radyolojik görüntüleme.")

# Q27 · yıkama sonrası kanama ara adımı
BRANCHES[43] = [{"when": "BLEEDING_AFTER_IRRIGATION", "goTo": "43a", "note": "Ara hemostaz, sonra tekrar yıkama"}]
review(43, 27, "Ara adım", "Yıkamadan sonra kanama görülürse monopolar koterle durdurulur, tekrar yıkanır; Adım 40'a dönülmez ama hazırlıkları tekrarlanır.")
review(40, 27, "Adaptif", "Kanamanın olup olmayacağı dijital ikize göre AI/adaptif motor tarafından seçilebilir.")
STEPS[40]["adaptiveCondition"] = "BLEEDING_DETECTED"

SUBFLOWS = [
    {"id": "HEMOSTASIS_RECHECK", "name": "Yıkama sonrası kanama", "reviewQuestion": 27, "entryFrom": 43, "returnsTo": 42,
     "steps": [
        {"id": "43a", "name": "Ara hemostaz: kanamanın monopolar koter ile durdurulması", "action": "HEMOSTASIS", "target": "BLEEDING_SITES",
         "performedBy": ["SURGEON"], "instrument": "ESU_MONOPOLAR_PEN", "energyMode": "COAG",
         "instruments": [I("ESU_MONOPOLAR_PEN", note="Koagülasyon modunda"), I("SUCTION_TIP_YANKAUER", "STANDBY"), I("IRRIGATION_SYRINGE", "STANDBY"),
                         I("RADIOPAQUE_LAP_SPONGE", "STANDBY"), I("STERILE_GAUZE_SPONGE", "STANDBY")],
         "notes": ["Adım 40'taki hazırlıklar (aspiratör, yıkama enjektörü, radyopak kompres, spanç) tekrarlanır."],
         "nextStep": 42, "branches": []},
     ]},
    {"id": "COUNT_DISCREPANCY", "name": "Sayım uyuşmazlığı", "reviewQuestion": 26, "entryFrom": 47, "returnsTo": 48,
     "steps": [
        {"id": "47a", "name": "Eksik malzemenin tekrar sayılması", "action": "COUNT", "target": "MISSING_ITEM",
         "performedBy": ["SCRUB", "CIRCULATOR"], "instrument": None, "instruments": [],
         "nextStep": "47b", "branches": [{"when": "ITEM_FOUND_ON_RECOUNT", "goTo": 47, "note": "Sayım hatasıydı; doğrulamaya dön"}]},
        {"id": "47b", "name": "Saha araması", "action": "SEARCH", "target": "SURGICAL_FIELD",
         "performedBy": ["SURGEON", "SCRUB", "CIRCULATOR"], "instrument": "HAND",
         "instruments": [I("HAND", note="Saha, örtüler, atık kovası"), I("SUCTION_TIP_YANKAUER", "STANDBY"), I("RETRACTOR_FARABEUF_15_SET", "IN_USE")],
         "nextStep": "47d", "branches": [{"when": "ITEM_FOUND_IN_FIELD", "goTo": "47c"}]},
        {"id": "47c", "name": "Bulunan malzemenin sayıma eklenmesi", "action": "COUNT", "target": "ALL_COUNTS",
         "performedBy": ["SCRUB", "CIRCULATOR"], "instrument": None, "instruments": [], "nextStep": 47, "branches": []},
        {"id": "47d", "name": "Radyolojik görüntüleme", "action": "IMAGING", "target": "PATIENT",
         "performedBy": ["SURGEON", "TEAM"], "instrument": None, "instruments": [],
         "nextStep": "47f", "branches": [{"when": "ITEM_FOUND_ON_IMAGING", "goTo": "47e"}]},
        {"id": "47e", "name": "Malzemenin hastadan çıkarılması; peritondan itibaren yeniden kapatma", "action": "RETRIEVE", "target": "MISSING_ITEM",
         "performedBy": ["SURGEON"], "instrument": "HAND", "instruments": [I("HAND"), I("RETRACTOR_FARABEUF_15_SET", "STANDBY")],
         "notes": ["Klinik teyit S26: görüntülemede bulunursa hasta tekrar açılır ve peritondan itibaren yeniden kapatılır."],
         "nextStep": 48, "branches": []},
        {"id": "47f", "name": "Görüntülemede bulunamadı: kayıt ve cerrah kararı", "action": "RECORD", "target": "PATIENT_RECORD",
         "performedBy": ["SURGEON", "CIRCULATOR"], "instrument": None, "instruments": [],
         "pendingClinicalConfirmation": ["Q-A5"], "notes": ["Kaynakta tanımlı değil; varsayılan olarak olay kaydı yapılıp kapanışa devam edilir."],
         "nextStep": 48, "branches": []},
     ]},
]

# Q28 · ameliyat öncesi notları (build.py PREOP'a uygular)
PREOP_NOTES = {
    "PRE03": "Klinik teyit S28: koter cihazının hazırlığı boyamadan önce yapılır.",
    "PRE07": "Klinik teyit S28: koter kalemi ve aspiratör hazırlığı hasta örtüldükten sonra steril olarak yapılır.",
    "PRE08": "Klinik teyit S28: koter kalemi ve aspiratör bağlantısı hasta örtüldükten sonra steril olarak yapılır.",
}

# ---------------------------------------------------------------- doğru uygulama metni düzeltmeleri
PRACTICE_OVERRIDES = {
    1: "Hekimin tercihine göre 10 veya 15 numaralı bistüri ucu 3 numaralı bistüri sapına güvenli şekilde takılır. Bistüri, sap kısmı cerraha dönük olacak şekilde uzatılır.",
    8: "Peritonun kontrollü biçimde tutulup yükseltilmesi için cerrahın tercihine göre Stille dişsiz penset 15 cm veya DeBakey diseksiyon penseti hazırlanır. Pensetin uç uyumu, yüzeyi, bütünlüğü ve sterilitesi kontrol edilir; güvenli biçimde uzatılır.",
    38: "Appendiks, tutulduğu Babcock klemp 16 cm ile birlikte sirkülenin hazırladığı %10 formaldehitli patoloji kabına asepsi bozulmadan bırakılır. Sirküle Babcock klempi kaptan alır ve kirli alanda tutar; alet ameliyat odasından çıkmaz ve kapanış sayımında sayılır.",
    50: "Kas tabakası için cerrahın istediği emilebilir sütür (1, 0 veya 2/0) hazırlanır; 2/0'dan ince sütür kullanılmaz. Scrub sütürün türünü, numarasını ve emilebilir olduğunu sözlü olarak doğrular, paket bütünlüğünü ve iğne-sütür bağlantısını kontrol eder, TC De Bakey portegü 18 cm'ye hazırlayıp güvenli biçimde uzatır. Yeni iğne sayım kaydına eklenir.",
}

# ---------------------------------------------------------------- hata senaryosu bayrakları
# SUPERSEDED: klinik teyitle çelişiyor, RAG'a girmez · CONDITIONAL: yalnızca belirtilen durumda geçerli · PENDING: teyit bekliyor
SCENARIO_FLAGS = {}
for k in range(1, 7):
    SCENARIO_FLAGS[f"S08-E{k}"] = {"status": "SUPERSEDED", "reviewQuestion": 9,
                                   "note": "Senaryo Crile klemp üzerine yazılmış; klinik teyitte periton penset ile tutuluyor. Yeni senaryo gerekiyor (Q-A3)."}
SCENARIO_FLAGS["S50-E3"] = {"status": "SUPERSEDED", "reviewQuestion": 5, "note": "2/0 sütür klinik teyitte kabul ediliyor."}
for e in ("S50-E1", "S50-E2"):
    SCENARIO_FLAGS[e] = {"status": "PENDING", "reviewQuestion": 5, "note": "Prolen'in kas tabakasında kullanımı teyit bekliyor (Q-A1)."}
for e in ("S48-E1", "S48-E2", "S48-E3", "S48-E4"):
    SCENARIO_FLAGS[e] = {"status": "CONDITIONAL", "reviewQuestion": 20, "applicableWhen": "Cerrah Crile klemp eğri 14 cm seçtiyse"}
SCENARIO_FLAGS["S13-E1"] = {"status": "CONDITIONAL", "reviewQuestion": 11,
                            "applicableWhen": "Vücut tipi kuralı ek ekartör gerektirmiyorsa (PATIENT_BODY_TYPE)"}
SCENARIO_FLAGS["S23-E1"] = {"status": "CONDITIONAL", "reviewQuestion": 15, "applicableWhen": "Orta çaplı damar için"}
SCENARIO_FLAGS["S23-E2"] = {"status": "CONDITIONAL", "reviewQuestion": 15,
                            "applicableWhen": "MESO_VESSELS iki damar içeriyorsa; adet damar sayısına göre değişir"}

# ---------------------------------------------------------------- yeni açık sorular
OPEN_QUESTIONS = [
    {"id": "Q-A1", "step": 50, "question": "Cevapta 'Prolen ve Vicryl emilebilir' denmiş. Prolen (polipropilen) emilmeyen bir iplik olarak bilinir. Kas tabakasında Prolen kullanılabilir mi, yoksa yalnızca Vicryl mi? Önerdiğiniz emilebilir/emilmeyen iplik listesini de gönderebilir misiniz?"},
    {"id": "Q-A2", "step": 50, "question": "'1, 0 veya 2' derken 2 numara değil 2/0 kastedildi mi? (Modelde 2/0 olarak işlendi; 2/0'dan ince iplik kullanılmıyor.)"},
    {"id": "Q-A3", "step": 8, "question": "Adım 8'in 6 hata senaryosu Crile klemp üzerine yazılmıştı ve geçersiz oldu. Stille/DeBakey penset için yeni hata senaryolarını kim yazacak? (Ben taslak çıkarabilirim, onayınız gerekir.)"},
    {"id": "Q-A4", "step": 13, "question": "Vücut tipine göre ekartör: zayıf hastada Farabeuf veya Langenbeck, normal kiloda Langenbeck yazıyor. Normal kilolu hastada Adım 11'de yine Farabeuf verilip 13'te Langenbeck mi ekleniyor? Kilolu hastada 'daha uzun uçlu ekartör' tam olarak hangisi (Richardson mı, uzun Langenbeck mi)?"},
    {"id": "Q-A5", "step": 47, "question": "Eksik malzeme radyolojik görüntülemede de bulunamazsa ne yapılıyor? Ayrıca sayım periton kapanmadan önce yapıldığı için 'tekrar açma' kapanış başladıktan sonraki bir sayımı mı ifade ediyor; cilt kapanmadan önce ikinci bir sayım var mı?"},
    {"id": "Q-A6", "step": 43, "question": "Yıkama sonrası kanama döngüsü en fazla kaç kez tekrarlanabilir? (Modelde varsayılan 2.)"},
]


# ================================================================ 2. TUR (Fatih Candemir, 2026-10-07 18:32–19:06)
VERSION = "2.1.0"
FILE_NAME = "open-appendectomy-v2.1.json"
REVIEW_SOURCE = {"file": "akis_sorulari.md + mesajlar", "receivedAt": "2026-10-07", "reviewer": "Fatih Candemir (klinik eğitmen)"}
PRACTICE_REPLACE = {}

def R2(n, decision, note=None):
    review(n, "R2", decision, note)

# --- Döngü sınırları modelde (simülasyon ilkesi: her tekrarın açık bir üst sınırı var)
GLOBAL_RULES.append({"id": "R09", "source": "clinical-review",
                     "text": "Simülasyonda her tekrarın ve döngünün açık bir üst sınırı vardır; sınır klinik ekip tarafından prosedüre özel belirlenir (genelleme yapılmaz)."})
BRANCHES[29][0]["maxIterations"] = 1          # varsayılan, teyit bekliyor (Q-B4)
BRANCHES[43][0]["maxIterations"] = 1          # kanama kontrolü yıkamayla birlikte en fazla 2 kez → ara hemostaz en fazla 1 kez
BRANCHES[47][0]["maxIterations"] = 1
R2(43, "Sınır", "Yıkama sonrası kanama kontrolü açık apendektomide 1 veya 2 kez yapılır; ara hemostaz döngüsü en fazla 1 kez.")
CONDITION_META["BLEEDING_AFTER_IRRIGATION"]["maxEvaluations"] = 2

# --- Adım 8: Excalidraw senaryoları Crile ile → hekim tercihi olarak iki seçenek (Q-B1 ile teyit)
STEPS[8]["inst"] = [I("FORCEPS_STILLE_NON_TOOTHED_15", "ALTERNATIVE"), I("FORCEPS_DEBAKEY", "ALTERNATIVE"),
                    I("CLAMP_CRILE_CURVED_14", "ALTERNATIVE", qty=2, note="Excalidraw; hata senaryoları bu seçenek için yazılmış")]
STEPS[8]["variants"] = [{"id": "PERITONEUM_GRASP", "options": ["Stille / DeBakey penset", "2 adet Crile klemp eğri 14 cm"], "decidedBy": "SURGEON"}]
STEPS[8]["pendingClinicalConfirmation"] = ["Q-B1"]
STEPS[8]["name"] = "Peritonun tutulması"
R2(8, "Hekim tercihi (teyit bekliyor)", "Excalidraw'daki hata senaryoları 2 adet eğri Crile üzerine yazılı; penset ile birlikte iki seçenek olarak tutuldu.")
for k in range(1, 7):
    SCENARIO_FLAGS[f"S08-E{k}"] = {"status": "CONDITIONAL", "reviewQuestion": "Q-B1",
                                   "applicableWhen": "Cerrah peritonu 2 adet Crile klemp eğri 14 cm ile tutmayı seçtiyse"}
PRACTICE_OVERRIDES[8] = ("Peritonun kontrollü biçimde tutulup yükseltilmesi için cerrahın tercihine göre Stille dişsiz penset 15 cm, DeBakey diseksiyon penseti "
                         "ya da iki adet Crile klemp eğri 14 cm hazırlanır. Aletin uç ve çene uyumu, kilit mekanizması, bütünlüğü ve sterilitesi kontrol edilir; "
                         "cerrahın istemine göre sırasıyla güvenli biçimde uzatılır.")

# --- Vücut tipi: kilolu hastada 15 cm'den uzun Farabeuf ve gerekirse 15 cm'den uzun Roux
INSTRUMENTS.update({
    "RETRACTOR_FARABEUF_LONG_SET": {"name": "Farabeuf ekartör seti, 15 cm'den uzun", "category": "retractor"},
    "RETRACTOR_ROUX_LONG": {"name": "Roux ekartör, 15 cm'den uzun", "category": "retractor"},
})
STEPS[13].pop("parameterRules", None)
STEPS[13]["newInstrument"] = False
SCENARIO_FLAGS.pop("S13-E1", None)
STEPS[11]["inst"].append(I("RETRACTOR_FARABEUF_LONG_SET", "ALTERNATIVE", note="Kilolu hasta"))
STEPS[11]["parameterRules"] = [{"parameter": "PATIENT_BODY_TYPE", "source": "Klinik teyit R2",
    "expectedInstruments": {"THIN": ["RETRACTOR_FARABEUF_15_SET"], "NORMAL": ["RETRACTOR_FARABEUF_15_SET"], "OBESE": ["RETRACTOR_FARABEUF_LONG_SET"]},
    "note": "Kilolu hastada 15 cm'den uzun Farabeuf; 15 cm uzatmak hata sayılır."}]
STEPS[12]["inst"].append(I("RETRACTOR_ROUX_LONG", "ALTERNATIVE", note="Kilolu hasta"))
STEPS[12]["parameterRules"] = [{"parameter": "PATIENT_BODY_TYPE", "source": "Klinik teyit R2",
    "expectedInstruments": {"THIN": ["RETRACTOR_ROUX_14", "RETRACTOR_ROUX_15"], "NORMAL": ["RETRACTOR_ROUX_14", "RETRACTOR_ROUX_15"], "OBESE": ["RETRACTOR_ROUX_LONG"]},
    "note": "Kilolu hastada gerekirse 15 cm'den uzun Roux."}]
for n in (11, 12, 13):
    R2(n, "Vücut tipi kuralı", "Kilolu hastada 15 cm'den uzun Farabeuf, gerekirse 15 cm'den uzun Roux. Adım 13'te yeni alet verilmez.")
STEPS[4]["pendingClinicalConfirmation"] = ["Q-B2"]

# --- Sayım: hiçbir yerde bulunamazsa paketten eksik çıktığı kabul edilir
for sf in SUBFLOWS:
    for x in sf["steps"]:
        if x["id"] == "47b":
            x["name"] = "Saha ve atık araması"
        if x["id"] == "47f":
            x["name"] = "Bulunamadı: paketten eksik çıktığı kabul edilir ve kayda geçer"
            x.pop("pendingClinicalConfirmation", None)
            x["notes"] = ["Klinik teyit R2: eksik malzeme tekrar sayımda, atıklarda ve radyolojik görüntülemede bulunamazsa paket içerisinden eksik çıktığı kabul edilir."]
R2(47, "Alt akış tamamlandı", "Hiçbir yerde bulunamayan malzeme paketten eksik kabul edilir.")

# --- Adım 50: yalnızca Vicryl 0 veya 1 ("1/0" ekipte 1 numara olarak kullanılıyor)
STEPS[50]["name"] = "Kas tabakasının 0 veya 1 Vicryl ile kapatılması"
STEPS[50]["inst"] = [I("NEEDLE_HOLDER_DEBAKEY_18"), I("SUTURE_POLYGLACTIN_0", "ALTERNATIVE"), I("SUTURE_POLYGLACTIN_1", "ALTERNATIVE"),
                     I("FORCEPS_SEMKEN_NON_TOOTHED_15_5", "SUPPORT")]
STEPS[50]["variants"] = [{"id": "SUTURE_SIZE", "options": ["0", "1"], "decidedBy": "SURGEON"}]
STEPS[50]["rules"] = [{"id": "R50-1", "text": "Kas tabakası emilebilir sütürle kapatılır: yalnızca Vicryl.", "check": "instrument.absorbable == true"},
                      {"id": "R50-2", "text": "Yalnızca 0 veya 1 numara; 2/0 ve daha ince kullanılmaz, Prolen kullanılmaz."}]
STEPS[50]["pendingClinicalConfirmation"] = ["Q-B5"]
R2(50, "Vicryl 0 veya 1", "Prolen kullanılmaz; yalnızca Vicryl 0 veya 1/0.")
for k in ("S50-E2", "S50-E3"):
    SCENARIO_FLAGS.pop(k, None)
SCENARIO_FLAGS["S50-E1"] = {"status": "SUPERSEDED", "reviewQuestion": "R2", "note": "Kas tabakasında Prolen istenmez."}
for k in ("S50-E3", "S50-E4", "S50-E5"):
    SCENARIO_FLAGS[k] = {"status": "ADAPTED", "reviewQuestion": "R2", "note": "Metin Vicryl-only kurala uyarlandı.",
                         "replace": [("1 veya 0 numara Prolen ya da Vicryl", "0 veya 1 numara Vicryl"), ("Prolen ya da Vicryl", "Vicryl"), ("Prolen veya Vicryl", "Vicryl")]}
PRACTICE_OVERRIDES[50] = ("Kas tabakası için cerrahın istediği 0 veya 1 numara Vicryl (emilebilir) hazırlanır; 2/0 ve daha ince sütür ile Prolen kullanılmaz. "
                          "Scrub sütürün türünü ve numarasını sözlü olarak doğrular, paket bütünlüğünü ve iğne-sütür bağlantısını kontrol eder, TC De Bakey portegü "
                          "18 cm'ye hazırlayıp güvenli biçimde uzatır. Doku desteği için Semken penset dişsiz 15,5 cm hazır bulundurulur. Yeni iğne sayım kaydına eklenir.")

# --- Adım 52: fasya Prolen yerine Vicryl (numara 0 korunur, Q-B3)
STEPS[52]["name"] = "Fasyanın 0 Vicryl ile kapatılması"
STEPS[52]["inst"] = [I("NEEDLE_HOLDER_DEBAKEY_18"), I("SUTURE_POLYGLACTIN_0", "SUPPORT"), I("FORCEPS_STILLE_NON_TOOTHED_15", "SUPPORT"),
                     I("FORCEPS_SEMKEN_NON_TOOTHED_15_5", "ALTERNATIVE")]
STEPS[52]["pendingClinicalConfirmation"] = ["Q-B3"]
R2(52, "Vicryl", "Fasyada Prolen yerine Vicryl kullanılır.")
STEPS[51]["inst"] = [i if i["code"] != "SUTURE_POLYPROPYLENE_0" else I("SUTURE_POLYGLACTIN_0", "STANDBY") for i in STEPS[51]["inst"]]
SCENARIO_FLAGS["S52-E1"] = {"status": "SUPERSEDED", "reviewQuestion": "R2", "note": "0 Vicryl artık doğru seçim."}
for k in ("S52-E2", "S52-E3", "S52-E4", "S52-E5"):
    SCENARIO_FLAGS[k] = {"status": "ADAPTED", "reviewQuestion": "R2", "note": "Prolen → Vicryl olarak uyarlandı.", "replace": [("Prolen", "Vicryl")]}
SCENARIO_FLAGS["S51-E1"] = {"status": "ADAPTED", "reviewQuestion": "R2", "note": "Sonraki adım adı güncellendi.", "replace": [("0 Prolen", "0 Vicryl")]}
PRACTICE_REPLACE[52] = [("Prolen", "Vicryl")]
PRACTICE_REPLACE[51] = [("0 Prolen", "0 Vicryl")]

# --- Adım 56: estetik → 4/0 Monocryl, standart → 3/0 veya 4/0 Prolen
INSTRUMENTS.update({
    "SUTURE_POLIGLECAPRONE_4_0": {"name": "4/0 Monocryl (iğneli)", "category": "suture", "material": "Poliglekapron 25", "absorbable": True,
                                  "inSet": False, "supply": "CONSUMABLE"},
    "SUTURE_POLYPROPYLENE_3_0": {"name": "3/0 Prolen (iğneli)", "category": "suture", "material": "Polipropilen", "absorbable": False,
                                 "inSet": False, "supply": "CONSUMABLE", "countable": True},
    "SUTURE_POLYPROPYLENE_4_0": {"name": "4/0 Prolen (iğneli)", "category": "suture", "material": "Polipropilen", "absorbable": False,
                                 "inSet": False, "supply": "CONSUMABLE", "countable": True},
})
INSTRUMENTS["SUTURE_POLIGLECAPRONE_4_0"]["countable"] = True
for code in ("SUTURE_POLYPROPYLENE_0", "SUTURE_POLYPROPYLENE_1"):
    INSTRUMENTS[code].pop("absorbableNote", None)
INSTRUMENTS["SUTURE_POLYAMIDE_4_0"]["note"] = "v2.1'de akışta kullanılmıyor (Adım 56 klinik teyitle değişti)"
PARAMETERS["SKIN_CLOSURE_TYPE"] = {"type": "enum", "values": ["STANDARD", "AESTHETIC"], "source": "SCENARIO", "default": "STANDARD",
                                   "note": "Cilt kapatma tipi; senaryo ya da cerrah istemi belirler"}
STEPS[56]["name"] = "Cildin kapatılması (estetik: 4/0 Monocryl, standart: 3/0 veya 4/0 Prolen)"
STEPS[56]["inst"] = [I("NEEDLE_HOLDER_DEBAKEY_18"),
                     I("SUTURE_POLIGLECAPRONE_4_0", "ALTERNATIVE", note="Estetik sütür"),
                     I("SUTURE_POLYPROPYLENE_3_0", "ALTERNATIVE", note="Standart sütür"),
                     I("SUTURE_POLYPROPYLENE_4_0", "ALTERNATIVE", note="Standart sütür"),
                     I("FORCEPS_STILLE_NON_TOOTHED_15", "SUPPORT"), I("FORCEPS_SEMKEN_NON_TOOTHED_15_5", "ALTERNATIVE")]
STEPS[56]["parameterRules"] = [{"parameter": "SKIN_CLOSURE_TYPE", "source": "Klinik teyit R2",
    "expectedInstruments": {"AESTHETIC": ["SUTURE_POLIGLECAPRONE_4_0"], "STANDARD": ["SUTURE_POLYPROPYLENE_3_0", "SUTURE_POLYPROPYLENE_4_0"]}}]
R2(56, "Kapatma tipine göre", "Estetik sütürde 4/0 Monocryl; normal sütürde 3/0 veya 4/0 Prolen.")
SCENARIO_FLAGS["S56-E1"] = {"status": "SUPERSEDED", "reviewQuestion": "R2", "note": "4/0 Polyamid artık kullanılmıyor."}
for k in ("S56-E2", "S56-E4"):
    SCENARIO_FLAGS[k] = {"status": "ADAPTED", "reviewQuestion": "R2", "note": "Polyamid → Prolen (standart kapatma) olarak uyarlandı.",
                         "applicableWhen": "Standart cilt kapatma (SKIN_CLOSURE_TYPE=STANDARD)", "replace": [("Polyamid", "Prolen")]}
SCENARIO_FLAGS["S55-E1"] = {"status": "ADAPTED", "reviewQuestion": "R2", "note": "Adım 56 sütürü güncellendi.",
                            "replace": [("4/0 Polyamid ile kapatılması", "kapatılması"), ("4/0 Polyamid", "kapatma tipine uygun cilt sütürü")]}
PRACTICE_OVERRIDES[56] = ("Cildin kapatılması için kapatma tipine göre sütür hazırlanır: estetik sütür atılacaksa 4/0 Monocryl, standart sütür atılacaksa 3/0 veya 4/0 Prolen. "
                          "Scrub sütürün türünü ve numarasını cerrahın istemiyle karşılaştırır, paket bütünlüğünü, sterilizasyon göstergesini, son kullanma tarihini ve "
                          "iğne-sütür bağlantısını kontrol eder; iğneli sütürü TC De Bakey portegü 18 cm'ye hazırlar. Doku desteği için Stille penset dişsiz 15 cm veya "
                          "Semken penset dişsiz 15,5 cm hazır bulundurulur. Kullanıma alınan her yeni iğne sayım kaydına eklenir.")
PRACTICE_REPLACE[55] = [("4/0 iğneli Polyamid", "kapatma tipine göre iğneli sütür (estetik: 4/0 Monocryl, standart: 3/0 veya 4/0 Prolen)")]

# --- İplik tablosu bekleniyor: emilme süresi alanı hazır
for code, d in INSTRUMENTS.items():
    if d["category"] == "suture":
        d.setdefault("absorptionDays", None)
        d["absorptionSource"] = "Bekleniyor: klinik ekibin iplik tablosu"

# --- Açık sorular (2. tur sonrası)
OPEN_QUESTIONS = [
    {"id": "Q-B1", "step": 8, "question": "İlk cevapta periton Stille/DeBakey penseti ile tutuluyordu; Excalidraw'daki hata senaryoları ise 2 adet eğri Crile klemp için yazılmış. İkisi de hekim tercihi olarak kabul mü? (Modelde şimdilik ikisi de kabul.)"},
    {"id": "Q-B2", "step": 4, "question": "Kilolu hastada uzun Farabeuf/Roux kuralı Adım 4'te (dış oblik aponevrozunun ortaya konması) de geçerli mi? Orada da Farabeuf 15 cm veya Roux 14/15 cm veriliyor."},
    {"id": "Q-B3", "step": 52, "question": "Fasyada Prolen yerine Vicryl: numara yine 0 mı?"},
    {"id": "Q-B4", "step": 29, "question": "Bağ güvenli bulunmazsa yeniden bağlama en fazla kaç kez yapılsın? (Simülasyon sınırı; şu an 1.)"},
    {"id": "Q-B5", "step": 50, "question": "'Vicryl 0 veya 1/0' ifadesindeki 1/0'ı 1 numara olarak işledim (PDF'te Adım 49'da da '0 veya 1' ile '0 veya 1/0' aynı anlamda geçiyor). Doğru mu?"},
]


# ================================================================ 3. TUR: iplik tablosu + yeni hata senaryosu taslakları (2026-10-07 20:02)
import json as _json, os as _os
VERSION = "2.2.0"
FILE_NAME = "open-appendectomy-v2.2.json"
REVIEW_SOURCE = {"file": "akis_sorulari.md + mesajlar + Cerrahi_süturaların_tablosu.docx", "receivedAt": "2026-10-07",
                 "reviewer": "Fatih Candemir (klinik eğitmen)"}

SUTURE_REFERENCE = _json.load(open(_os.path.join(_os.path.dirname(__file__), "ref", "suture_reference.json")))
for e in SUTURE_REFERENCE["sutures"]:
    if e["name"].startswith("İpek"):
        e["brands"] = e["material"]
        e["material"] = "İpek"
_REF = {e["name"]: e for e in SUTURE_REFERENCE["sutures"]}
SUTURE_REF_MAP = {
    "SUTURE_POLYGLACTIN_2_0": "Vicryl", "SUTURE_POLYGLACTIN_0": "Vicryl", "SUTURE_POLYGLACTIN_1": "Vicryl",
    "SUTURE_POLIGLECAPRONE_4_0": "Monocryl",
    "SUTURE_POLYPROPYLENE_0": "Prolene / Surgipro", "SUTURE_POLYPROPYLENE_1": "Prolene / Surgipro",
    "SUTURE_POLYPROPYLENE_3_0": "Prolene / Surgipro", "SUTURE_POLYPROPYLENE_4_0": "Prolene / Surgipro",
    "SUTURE_SILK_3_0": "İpek / Silk", "SUTURE_POLYAMIDE_4_0": "Ethilon / Dermalon / Dafilon / Monosof",
}
# Senaryolarda "yanlış seçim" olarak kullanılan, akışta kabul edilmeyen iplik
INSTRUMENTS["SUTURE_POLYGLACTIN_RAPIDE"] = {"name": "Vicryl Rapide (hızlı emilen poliglaktin 910)", "category": "suture",
                                            "inSet": False, "supply": "CONSUMABLE", "countable": True,
                                            "note": "Akışta kabul edilmez; hata senaryolarında karıştırılan iplik olarak geçer"}
SUTURE_REF_MAP["SUTURE_POLYGLACTIN_RAPIDE"] = "Vicryl Rapide"
for code, refname in SUTURE_REF_MAP.items():
    r = _REF[refname]
    INSTRUMENTS[code].update({"material": r["material"], "absorbable": r["absorbable"], "structure": r["structure"],
                              "absorptionDays": r["absorptionDays"], "referenceName": refname,
                              "absorptionSource": "Cerrahi sütürler tablosu (klinik ekip, 2026-10-07)"})

# ---- Yeni hata senaryosu taslakları (klinik onay bekliyor; RAG'a girmez)
def _sc(id_, replaces, title, err, comp, signs, steps, cont, org):
    return {"id": id_, "title": title, "scrubError": err, "possibleComplications": comp, "signs": signs,
            "resolutionSteps": steps, "caseContinuation": cont, "organizationalImpact": org,
            "_draft": {"replaces": replaces, "basis": "Excalidraw senaryo yapısı + klinik teyit R2 + iplik tablosu"}}

_ORG_STD = ["Yanlış sütürün değiştirilmesi zaman kaybına yol açabilir.",
            "Açılan sütür paketi sarf kaybına neden olur.",
            "Yeni iğnenin sayım kaydına eklenmesi gerekir.",
            "Ameliyat ve anestezi süresi uzayabilir.",
            "Sütürlerin masada tür ve numaraya göre düzeni yeniden gözden geçirilebilir."]

DRAFT_SCENARIOS = {
 50: [
  _sc("S50-D1", "S50-E2", "Kas tabakası için emilmeyen sütür (Prolen) hazırlanması",
      "Cerrah kas tabakası için 0 veya 1 numara Vicryl istediği hâlde scrub, sütürün emilebilir olup olmadığını kontrol etmeden aynı numarada Prolen hazırlar.",
      ["Kas tabakasında emilmeyen bir materyal kullanılmış olur; sütür dokuda kalıcı olarak kalır.",
       "Cerrahın planladığı kapatma tekniğine aykırı materyal seçilmiş olur.",
       "Kalıcı sütüre bağlı yabancı cisim reaksiyonu, ağrı veya ele gelen düğüm gelişebilir.",
       "Kapatmanın durdurulması ve sütürlerin değiştirilmesi gerekebilir.",
       "Ek sütür ve iğne kullanılabilir.",
       "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."],
      ["Sütür ambalajında “Prolen / polipropilen” yazdığının görülmesi.",
       "Sütürün genellikle mavi renkli ve tek telli (monofilament) olması; Vicryl ise örgülüdür.",
       "Cerrahın Vicryl istemini tekrarlaması.",
       "Scrub ile sirküle hemşirenin farklı sütür türü bildirmesi."],
      ["Scrub, “Kas tabakası için Vicryl yerine emilmeyen Prolen hazırlandı” diyerek hatayı bildirir.",
       "Yanlış sütürü kullanıma sunmadan güvenli alana geri alır.",
       "Kullanıldıysa cerrahın kapatma hattını değerlendirmesini ve sütürlerin değiştirilip değiştirilmeyeceğine karar vermesini bekler.",
       "Portegü üzerindeki iğneyi kesici-delici alet güvenliğini koruyarak kontrol altında tutar.",
       "Yanlış sütürün iğnesini sirküle hemşireyle birlikte sayıma dâhil eder.",
       "Cerrahın istediği sütür türünü, numarasını ve emilebilir olduğunu sözlü olarak yeniden doğrular.",
       "0 veya 1 numara iğneli Vicryl paketini seçer; paket bütünlüğünü, sterilizasyon göstergesini ve son kullanma tarihini kontrol eder.",
       "Vicryl’i TC De Bakey portegü 18 cm’ye hazırlar ve yeni iğneyi sayım kaydına ekletir.",
       "İğne takılı portegüyü güvenli biçimde cerraha uzatır.",
       "Gerçekleştirilen düzeltme kayıt altına alınır."],
      "Emilebilir 0 veya 1 numara Vicryl hazırlanıp cerraha uzatılır. Cerrah kapatma hattını değerlendirdikten sonra Adım 51 – Fasya kenarlarının hazırlanması aşamasına geçilir.",
      _ORG_STD),
  _sc("S50-D2", None, "Vicryl yerine hızlı emilen Vicryl Rapide hazırlanması",
      "Cerrah kas tabakası için 0 veya 1 numara Vicryl istediği hâlde scrub, ambalajları benzer olduğu için ürün adını okumadan Vicryl Rapide hazırlar.",
      ["Vicryl Rapide hızlı emilen bir poliglaktin 910 türüdür; tabloya göre emilme süresi yaklaşık 42 gün, Vicryl’de 56–70 gündür.",
       "Sütürün dayanıklılığı kas tabakasının iyileşmesi tamamlanmadan azalabilir.",
       "Kapatma hattında erken gevşeme veya ayrılma riski oluşabilir.",
       "Sütürlerin değiştirilmesi ve ek sütür kullanılması gerekebilir.",
       "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."],
      ["Ambalajda “Rapide” ibaresinin görülmesi.",
       "Ambalaj rengi veya ürün kodunun istenen Vicryl’den farklı olması.",
       "Cerrahın ürün adını sorması veya Vicryl istemini tekrarlaması."],
      ["Scrub, “Vicryl yerine Vicryl Rapide hazırlandı” diyerek hatayı bildirir.",
       "Yanlış sütürü kullanıma sunmadan geri alır; kullanıldıysa cerrahın değerlendirmesini bekler.",
       "İğneyi kesici-delici alet güvenliğini koruyarak kontrol altında tutar ve sayıma dâhil eder.",
       "Ambalaj üzerindeki ürün adını, numarayı ve emilme özelliğini sirküle hemşireyle birlikte okur.",
       "İstenen 0 veya 1 numara Vicryl’i hazırlar, paket bütünlüğünü ve son kullanma tarihini kontrol eder.",
       "Vicryl’i TC De Bakey portegü 18 cm’ye hazırlar, yeni iğneyi sayım kaydına ekletir ve güvenli biçimde uzatır.",
       "Benzer ambalajlı sütürleri masada ayrı tutarak tekrarını önler."],
      "Doğru Vicryl hazırlanıp cerraha uzatılır. Kapatma hattı değerlendirildikten sonra Adım 51’e geçilir.",
      _ORG_STD + ["Benzer ambalajlı ürünlerin depoda ve masada ayrı yerleşimi gözden geçirilebilir."]),
 ],
 52: [
  _sc("S52-D1", "S52-E1", "Fasya için 0 Vicryl yerine Prolen hazırlanması",
      "Cerrah fasyanın kapatılması için 0 numara Vicryl istediği hâlde scrub, sütür türünü kontrol etmeden 0 Prolen hazırlar.",
      ["Cerrahın istemine aykırı, emilmeyen bir materyal hazırlanmış olur.",
       "Fasyada planlanmayan kalıcı sütür kalabilir; ele gelen düğüm veya yabancı cisim reaksiyonu gelişebilir.",
       "Kapatmanın durdurulması ve sütürlerin değiştirilmesi gerekebilir.",
       "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."],
      ["Sütür ambalajında “Prolen / polipropilen” yazdığının görülmesi.",
       "Sütürün tek telli ve genellikle mavi renkli olması.",
       "Cerrahın 0 Vicryl istemini tekrarlaması."],
      ["Scrub, “Fasya için 0 Vicryl yerine Prolen hazırlandı” diyerek hatayı bildirir.",
       "Yanlış sütürü kullanıma sunmadan geri alır; kullanıldıysa cerrahın kapatma hattını değerlendirmesini bekler.",
       "İğneyi kesici-delici alet güvenliğini koruyarak kontrol altında tutar ve sayıma dâhil eder.",
       "Cerrahın istediği sütür türünü ve numarasını sözlü olarak yeniden doğrular.",
       "0 numara iğneli Vicryl paketini seçer; paket bütünlüğünü, sterilizasyon göstergesini ve son kullanma tarihini kontrol eder.",
       "Vicryl’i TC De Bakey portegü 18 cm’ye hazırlar, yeni iğneyi sayım kaydına ekletir ve güvenli biçimde uzatır.",
       "Gerçekleştirilen düzeltme kayıt altına alınır."],
      "0 Vicryl hazırlanıp cerraha uzatılır. Cerrah fasya kapatma hattını değerlendirdikten sonra Adım 53 – Cilt altının serum fizyolojik ile yıkanması aşamasına geçilir.",
      _ORG_STD),
  _sc("S52-D2", None, "Fasya için Vicryl yerine hızlı emilen Vicryl Rapide hazırlanması",
      "Cerrah fasya için 0 numara Vicryl istediği hâlde scrub, ürün adını okumadan 0 Vicryl Rapide hazırlar.",
      ["Tabloya göre Vicryl Rapide yaklaşık 42 günde, Vicryl 56–70 günde emilir; fasya iyileşmesi tamamlanmadan sütür dayanıklılığı azalabilir.",
       "Fasya kapatma hattında ayrılma ve ileride fıtık gelişme riski artabilir.",
       "Sütürlerin değiştirilmesi gerekebilir.",
       "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."],
      ["Ambalajda “Rapide” ibaresinin görülmesi.",
       "Ambalaj rengi veya ürün kodunun istenenden farklı olması.",
       "Cerrahın ürün adını sorması."],
      ["Scrub, “Vicryl yerine Vicryl Rapide hazırlandı” diyerek hatayı bildirir.",
       "Yanlış sütürü kullanıma sunmadan geri alır; kullanıldıysa cerrahın değerlendirmesini bekler.",
       "İğneyi kontrol altında tutar ve sayıma dâhil eder.",
       "Ürün adını, numarayı ve emilme özelliğini sirküle hemşireyle birlikte okur.",
       "0 numara Vicryl’i hazırlar, kontrollerini yapar, yeni iğneyi sayıma ekletir ve güvenli biçimde uzatır."],
      "Doğru 0 Vicryl ile fasya kapatılır. Kapatma hattı değerlendirildikten sonra Adım 53’e geçilir.",
      _ORG_STD),
 ],
 56: [
  _sc("S56-D1", "S56-E1", "Estetik sütür istendiği hâlde Prolen hazırlanması",
      "Cerrah estetik cilt kapatma için 4/0 Monocryl istediği hâlde scrub, kapatma tipini doğrulamadan 4/0 Prolen hazırlar.",
      ["Estetik kapatma için planlanan emilebilir sütür yerine emilmeyen sütür hazırlanmış olur.",
       "Emilmeyen cilt sütürleri sonradan alınmayı gerektirir; estetik sonuç planlanandan farklı olabilir.",
       "Kapatmanın yeniden düzenlenmesi gerekebilir.",
       "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."],
      ["Ambalajda “Prolen” yazdığının görülmesi.",
       "Cerrahın estetik sütür ve Monocryl istemini tekrarlaması.",
       "Scrub ile sirküle hemşirenin farklı kapatma tipi bildirmesi."],
      ["Scrub, “Estetik sütür için Monocryl yerine Prolen hazırlandı” diyerek hatayı bildirir.",
       "Yanlış sütürü kullanıma sunmadan geri alır; kullanıldıysa cerrahın değerlendirmesini bekler.",
       "İğneyi kontrol altında tutar ve sayıma dâhil eder.",
       "Cerrahın istediği kapatma tipini (estetik / standart) ve sütürü sözlü olarak yeniden doğrular.",
       "4/0 Monocryl’i hazırlar; paket bütünlüğünü, sterilizasyon göstergesini ve son kullanma tarihini kontrol eder.",
       "Sütürü TC De Bakey portegü 18 cm’ye hazırlar, yeni iğneyi sayım kaydına ekletir ve güvenli biçimde uzatır."],
      "4/0 Monocryl ile estetik cilt kapatma yapılır. Ardından Adım 57 – Ameliyat sahasının ıslak tampon ile temizlenmesi aşamasına geçilir.",
      _ORG_STD),
  _sc("S56-D2", None, "Standart sütür istendiği hâlde Monocryl hazırlanması",
      "Cerrah standart cilt kapatma için 3/0 veya 4/0 Prolen istediği hâlde scrub, kapatma tipini doğrulamadan 4/0 Monocryl hazırlar.",
      ["Cerrahın planladığı kapatma tekniğine uymayan sütür hazırlanmış olur.",
       "Kapatma gecikebilir ve sütür değiştirilmesi gerekebilir.",
       "Açılan paket nedeniyle sarf kaybı oluşur.",
       "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."],
      ["Ambalajda “Monocryl” yazdığının görülmesi.",
       "Cerrahın Prolen istemini tekrarlaması.",
       "Scrub ile sirküle hemşirenin farklı kapatma tipi bildirmesi."],
      ["Scrub, “Standart sütür için Prolen yerine Monocryl hazırlandı” diyerek hatayı bildirir.",
       "Yanlış sütürü kullanıma sunmadan geri alır ve iğneyi sayıma dâhil eder.",
       "Cerrahın istediği kapatma tipini ve sütür numarasını (3/0 veya 4/0) sözlü olarak yeniden doğrular.",
       "İstenen Prolen’i hazırlar, kontrollerini yapar, yeni iğneyi sayıma ekletir ve güvenli biçimde uzatır."],
      "İstenen Prolen ile standart cilt kapatma yapılır. Ardından Adım 57’ye geçilir.",
      _ORG_STD),
 ],
}
for n in DRAFT_SCENARIOS:
    STEPS[n].setdefault("pendingClinicalConfirmation", []).append("Q-C1")

OPEN_QUESTIONS = OPEN_QUESTIONS + [
    {"id": "Q-C1", "steps": [50, 52, 56], "question": "Adım 50, 52 ve 56 için 6 yeni hata senaryosu taslağı yazıldı (acik-sorular / hata-senaryolari-taslak dosyası). Onaylanırsa yerini aldıkları eski senaryolar (S50-E2, S52-E1, S56-E1) devreden çıkar."},
]
INSTRUMENTS["LIGATURE_SURGEON_CHOICE"].update({"absorbable": None, "absorptionDays": None,
                                               "absorbableNote": "Cerrahın seçimine bağlı; seçilen ipliğin bilgisi sutureReference'tan okunur"})


# ================================================================ 4. TUR: onay Excel'i (Fatih Candemir, 2026-10-09 22:01)
VERSION = "2.3.0"
FILE_NAME = "open-appendectomy-v2.3.json"
REVIEW_SOURCE = {"file": "akis_sorulari.md + mesajlar + iplik tablosu + hata-senaryolari-onay-v2.2 (doldurulmuş)", "receivedAt": "2026-10-09",
                 "reviewer": "Fatih Candemir (klinik eğitmen)"}

def _draft(sid):
    for n, lst in DRAFT_SCENARIOS.items():
        for x in lst:
            if x["id"] == sid:
                return x
    raise KeyError(sid)

# --- Hoca'nın Excel'de yaptığı metin düzeltmeleri
_draft("S50-D1")["title"] = "Kas tabakası için emilebilen Vicryl (0 veya 1/0) yerine emilmeyen Prolen hazırlanması"
_draft("S52-D1")["possibleComplications"] = [
    "Cerrahın istemine aykırı, emilmeyen bir materyal hazırlanmış olur.",
    "Kalıcı sütüre bağlı yabancı cisim reaksiyonu, ağrı veya ele gelen düğüm gelişebilir.",
    "Kapatmanın durdurulması ve sütürlerin değiştirilmesi gerekebilir.",
    "Yanlış materyal kullanılmadan fark edilirse olay ramak kala olarak yönetilir."]
_d = _draft("S56-D2")
_d["title"] = "4/0 Polyamid sütür istendiği hâlde Monocryl hazırlanması"
_d["scrubError"] = "Cerrah standart cilt kapatma için 4/0 Polyamid istediği hâlde scrub, kapatma tipini doğrulamadan 4/0 Monocryl hazırlar."
_d["signs"] = ["Ambalajda “Monocryl” yazdığının görülmesi.", "Cerrahın Polyamid istemini tekrarlaması.",
               "Scrub ile sirküle hemşirenin farklı kapatma tipi bildirmesi."]
_d["resolutionSteps"] = ["Scrub, “Standart sütür için Polyamid yerine Monocryl hazırlandı” diyerek hatayı bildirir.",
                         "Yanlış sütürü kullanıma sunmadan geri alır ve iğneyi sayıma dâhil eder.",
                         "Cerrahın istediği kapatma tipini ve sütür numarasını (4/0 Polyamid) sözlü olarak yeniden doğrular.",
                         "İstenen 4/0 Polyamid’i hazırlar, kontrollerini yapar, yeni iğneyi sayıma ekletir ve güvenli biçimde uzatır."]
_d["caseContinuation"] = "İstenen Polyamid ile standart cilt kapatma yapılır. Ardından Adım 57’ye geçilir."

# --- Onaylanan taslaklar kalıcı kimlik alır ve aktif olur
APPROVED_DRAFTS = {
    "S50-D1": {"newId": "S50-E6", "decision": "Onay (başlık düzeltildi)"},
    "S50-D2": {"newId": "S50-E7", "decision": "Onay"},
    "S52-D1": {"newId": "S52-E6", "decision": "Onay (komplikasyonlar düzeltildi)"},
    "S52-D2": {"newId": "S52-E7", "decision": "Onay"},
    "S56-D1": {"newId": "S56-E5", "decision": "Onay"},
    "S56-D2": {"newId": "S56-E6", "decision": "Düzeltme uygulandı (Prolen → 4/0 Polyamid)"},
}
APPROVAL_META = {"approvedBy": "Fatih Candemir", "approvedAt": "2026-10-09", "source": "hata-senaryolari-onay-v2.2.xlsx"}
SCENARIO_FLAGS["S50-E2"] = {"status": "SUPERSEDED", "reviewQuestion": "R4", "note": "Yerini onaylı S50-E6 aldı."}
for n in (50, 52, 56):
    STEPS[n]["pendingClinicalConfirmation"] = [q for q in STEPS[n].get("pendingClinicalConfirmation", []) if q != "Q-C1"]
    R2(n, "Senaryolar onaylandı", "Excel onayı: yeni hata senaryoları aktif.")

# --- Adım 56: standart kapatmada 4/0 Polyamid de kabul (7 Ekim'de Prolen denmişti → Q-D1)
INSTRUMENTS["SUTURE_POLYAMIDE_4_0"]["note"] = "Standart cilt kapatmada kabul (S56-E6 onayı); Prolen ile ilişkisi teyit bekliyor (Q-D1)"
STEPS[56]["inst"].insert(4, I("SUTURE_POLYAMIDE_4_0", "ALTERNATIVE", note="Standart sütür; teyit bekliyor (Q-D1)"))
STEPS[56]["parameterRules"][0]["expectedInstruments"]["STANDARD"] = ["SUTURE_POLYPROPYLENE_3_0", "SUTURE_POLYPROPYLENE_4_0", "SUTURE_POLYAMIDE_4_0"]
STEPS[56]["name"] = "Cildin kapatılması (estetik: 4/0 Monocryl, standart: 3/0–4/0 Prolen veya 4/0 Polyamid)"
STEPS[56]["pendingClinicalConfirmation"] = ["Q-D1"]
PRACTICE_OVERRIDES[56] = PRACTICE_OVERRIDES[56].replace("standart sütür atılacaksa 3/0 veya 4/0 Prolen",
                                                        "standart sütür atılacaksa 3/0 veya 4/0 Prolen ya da 4/0 Polyamid")
PRACTICE_REPLACE[55] = [("4/0 iğneli Polyamid", "kapatma tipine göre iğneli sütür (estetik: 4/0 Monocryl, standart: 3/0–4/0 Prolen veya 4/0 Polyamid)")]

OPEN_QUESTIONS = [q for q in OPEN_QUESTIONS if q["id"] != "Q-C1"] + [
    {"id": "Q-D1", "step": 56, "question": "7 Ekim'de standart cilt kapatma için '3/0 veya 4/0 Prolen' dediniz; onay Excel'inde ise standart sütürü 4/0 Polyamid olarak düzelttiniz. Standart kapatmada hangisi kullanılıyor: yalnızca Prolen mi, yalnızca 4/0 Polyamid mi, yoksa ikisi de mi (hekim tercihi)?"},
]

# =====================================================================
# 5. tur (2026-10-09): kalan açık sorular proje kararıyla kapatıldı.
# Kaynak: Mennan Sevim'in talebi ("en mantıklısı hangisiyse seç").
# Q-B3 dışındakiler klinik ekipçe ayrıca teyit EDİLMEDİ; basis alanında gerekçe var.
# =====================================================================
VERSION = "2.4.0"; FILE_NAME = "open-appendectomy-v2.4.json"
DECISION_META = {"decidedBy": "Mennan Sevim (proje kararı)", "decidedAt": "2026-10-09"}

RESOLVED_QUESTIONS = [
    {"id": "Q-B1", "step": 8, "resolution": "Penset (Stille/DeBakey) ve 2 adet eğri Crile klemp ikisi de kabul; hekim tercihi.",
     "basis": "Fatih'in 1. cevabı penset, Excalidraw senaryoları Crile; 7 Ekim mesajında 'penset ya da 2 adet eğri Crile' ifadesi.", "clinicallyConfirmed": False},
    {"id": "Q-B2", "step": 4, "resolution": "Kilolu hastada uzun ekartör kuralı Adım 4'te de geçerli: 15 cm'den uzun Farabeuf, gerekirse 15 cm'den uzun Roux.",
     "basis": "Adım 4 ile Adım 11-12 aynı ekartörleri kullanıyor; kuralın amacı (derin yağ dokusunda ekspozisyon) Adım 4'te de geçerli.", "clinicallyConfirmed": False},
    {"id": "Q-B3", "step": 52, "resolution": "Fasya 0 numara Vicryl ile kapatılır.",
     "basis": "Fatih'in onayladığı S52-E6 başlığı: 'Fasya için 0 Vicryl yerine Prolen hazırlanması'.", "clinicallyConfirmed": True},
    {"id": "Q-B4", "step": 29, "resolution": "Yeniden bağlama en fazla 1 kez.",
     "basis": "Kanama ve sayım döngüleriyle aynı sınır (R09); ikinci başarısızlık simülasyonda vaka sonu/eğitmen müdahalesi olarak ele alınır.", "clinicallyConfirmed": False},
    {"id": "Q-B5", "step": 50, "resolution": "'1/0' 1 numara olarak işlendi; kabul edilen Vicryl numaraları 0 ve 1.",
     "basis": "PDF Adım 49'da '0 veya 1' ile '0 veya 1/0' aynı anlamda geçiyor; 0 ve 1 numara kas kapatmada standart.", "clinicallyConfirmed": False},
    {"id": "Q-D1", "step": 56, "resolution": "Standart cilt kapatmada 3/0 Prolen, 4/0 Prolen ve 4/0 Polyamid üçü de kabul; hekim tercihi.",
     "basis": "7 Ekim mesajı Prolen, onay Excel'i 4/0 Polyamid diyor; ikisi de emilmeyen tek telli cilt sütürü, birbirinin yerine kullanılabilir.", "clinicallyConfirmed": False},
]
OPEN_QUESTIONS = []

def R5(n, decision, note=None):
    review(n, "R5", decision, note)

for q in RESOLVED_QUESTIONS:
    STEPS[q["step"]]["pendingClinicalConfirmation"] = [x for x in STEPS[q["step"]].get("pendingClinicalConfirmation", []) if x != q["id"]]
    if not STEPS[q["step"]]["pendingClinicalConfirmation"]:
        STEPS[q["step"]].pop("pendingClinicalConfirmation")
    R5(q["step"], f"{q['id']} kapandı", q["resolution"])

# Q-B1: Adım 8
for r in STEPS[8]["review"]:
    if r["decision"] == "Hekim tercihi (teyit bekliyor)":
        r["decision"] = "Hekim tercihi"
for k in range(1, 7):
    SCENARIO_FLAGS[f"S08-E{k}"]["reviewQuestion"] = "R5"

# Q-B2: Adım 4'e vücut tipi kuralı
STEPS[4]["inst"] += [I("RETRACTOR_FARABEUF_LONG_SET", "ALTERNATIVE", note="Kilolu hasta"),
                     I("RETRACTOR_ROUX_LONG", "ALTERNATIVE", note="Kilolu hasta, gerekirse")]
# Roux da kurala dahil (2026-10-14): kural yalnız Farabeuf'u listelediği için kilolu hastada Roux 14/15 kabul ediliyordu.
STEPS[4]["parameterRules"] = [{"parameter": "PATIENT_BODY_TYPE", "source": "R5 (Q-B2)",
    "expectedInstruments": {"THIN": ["RETRACTOR_FARABEUF_15_SET", "RETRACTOR_ROUX_14", "RETRACTOR_ROUX_15"],
                            "NORMAL": ["RETRACTOR_FARABEUF_15_SET", "RETRACTOR_ROUX_14", "RETRACTOR_ROUX_15"],
                            "OBESE": ["RETRACTOR_FARABEUF_LONG_SET", "RETRACTOR_ROUX_LONG"]},
    "note": "Kilolu hastada 15 cm'den uzun Farabeuf, gerekirse 15 cm'den uzun Roux; 15 cm uzatmak hata sayılır."}]

# Q-B4: döngü sınırı kesinleşti (değer zaten 1)
BRANCHES[29][0]["maxIterations"] = 1

# Q-D1: Adım 56 notları
INSTRUMENTS["SUTURE_POLYAMIDE_4_0"]["note"] = "Standart cilt kapatmada Prolen'e alternatif (hekim tercihi)"
for i in STEPS[56]["inst"]:
    if i["code"] == "SUTURE_POLYAMIDE_4_0":
        i["note"] = "Standart sütür, hekim tercihi"
for c in STEPS[50].get("conflicts", []):
    if "Q-A1 ile teyit bekliyor" in c.get("resolution", ""):
        c["resolution"] = "Klinik teyit (S5, R2): emilebilir iplik zorunlu; yalnızca 0 veya 1 numara Vicryl, Prolen kullanılmaz."

# ================================================================ 6. TUR (2026-10-14, proje kararı): yanıltıcı aletler
# Onaylı hata senaryolarındaki yanlış seçimler (ör. S01-E1 11 numaralı bistüri, S10-E1 Mayo makas, S20-E1 Overholt)
# simülatörde yapılabilsin diye katalogda durur. Hiçbir adımın alet listesine girmez; motor her adımda reddeder.
DISTRACTORS = {
    "SCALPEL_BLADE_11": ({"name": "No:11 bistüri ucu (sivri)", "category": "blade"}, ["S01-E1", "S05-E1", "S09-E1", "S31-E1"]),
    "SCALPEL_BLADE_12": ({"name": "No:12 bistüri ucu (kanca)", "category": "blade"}, ["S31-E1"]),
    "FORCEPS_TOOTHED_15": ({"name": "Dişli penset (cerrahi penset) 15 cm", "category": "forceps"},
                           ["S02-E2", "S03-E1", "S05-E5", "S27-E1", "S55-E2"]),
    "SCISSORS_MAYO": ({"name": "Mayo makas", "category": "scissors"}, ["S07-E4", "S10-E1", "S24-E2"]),
    "CLAMP_OVERHOLT_GEISSENDOERFER_21": ({"name": "Overholt-Geissendörfer klemp eğri 21 cm", "category": "clamp"}, ["S20-E1"]),
    "SUTURE_POLYPROPYLENE_2_0": ({"name": "2/0 Prolen (iğneli)", "category": "suture", "countable": True}, ["S54-E1", "S56-E2"]),
    "SUTURE_POLYGLACTIN_3_0": ({"name": "3/0 Vicryl (iğneli)", "category": "suture", "countable": True}, ["S26-E1", "S28-E1"]),
    "CHLORHEXIDINE_SOLUTION": ({"name": "Klorheksidinli antiseptik solüsyon", "category": "antiseptic"},
                               ["S33-E1", "S42-E1", "S53-E1", "S57-E1"]),
}
for code, (d, scenarios) in DISTRACTORS.items():
    INSTRUMENTS[code] = {**d, "inSet": False, "supply": "CONSUMABLE" if d["category"] == "suture" else "DISTRACTOR",
                         "distractor": True, "scenarios": scenarios,
                         "note": "Akışta kabul edilmez; hata senaryolarında karıştırılan alet olarak geçer"}
for code, refname in {"SUTURE_POLYPROPYLENE_2_0": "Prolene / Surgipro", "SUTURE_POLYGLACTIN_3_0": "Vicryl"}.items():
    r = _REF[refname]
    INSTRUMENTS[code].update({"material": r["material"], "absorbable": r["absorbable"], "structure": r["structure"],
                              "absorptionDays": r["absorptionDays"], "referenceName": refname,
                              "absorptionSource": "Cerrahi sütürler tablosu (klinik ekip, 2026-10-07)"})

# ================================================================ 7. TUR (2026-10-14, proje kararı): hekim tercihi
# Genel ilke (cerrah tercih kartı + kapalı döngü iletişim, AORN): seçenekler alet TİPİ, BOYU ya da İPLİK NUMARASI bakımından
# farklıysa seçimi cerrah yapar ve sözlü olarak ister; scrub istemi tekrar edip doğrular ve yalnızca isteneni uzatır.
# Aynı işi gören eşdeğer aletler (ör. iki dişsiz penset Stille/Semken) tek seçenektir; hangisi uzatılsa doğrudur.
# "instruments": her seçeneğin alet kodları (seçenek başına bir liste). Backend motoru cerrah istemini bu listeden belirler;
# varyantı olmayan adımda kabul edilen her alternatif doğrudur (ör. Adım 3 Stille/Semken, Adım 36 Crile düz/Kelly-Rankin).
def surgeon_choice(n, vid, options, instruments, basis):
    variants = STEPS[n].setdefault("variants", [])
    v = next((x for x in variants if x["id"] == vid), None)
    if v is None:
        v = {"id": vid, "options": options, "decidedBy": "SURGEON"}
        variants.append(v)
    v.update({"instruments": instruments, "basis": basis})

surgeon_choice(1, "SKIN_BLADE", ["No:10", "No:15"], [["SCALPEL_BLADE_10"], ["SCALPEL_BLADE_15"]],
               "Bistüri ucu numarası tercih kartındadır; onaylı S01-E1")
surgeon_choice(4, "RETRACTOR", ["Farabeuf", "Roux 14 cm", "Roux 15 cm", "Roux uzun"],
               [["RETRACTOR_FARABEUF_15_SET", "RETRACTOR_FARABEUF_LONG_SET"], ["RETRACTOR_ROUX_14"], ["RETRACTOR_ROUX_15"], ["RETRACTOR_ROUX_LONG"]],
               "Ekartör tipi ve Roux boyu cerrahın istemidir (boyu hasta tipi de sınırlar); onaylı S04-E2")
surgeon_choice(8, "PERITONEUM_GRASP", ["Stille / DeBakey penset", "2 adet Crile klemp eğri 14 cm"],
               [["FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_DEBAKEY"], ["CLAMP_CRILE_CURVED_14"]],
               "Q-B1 hekim tercihi; iki dişsiz penset eşdeğerdir")
surgeon_choice(12, "ROUX_SIZE", ["Roux 14 cm", "Roux 15 cm", "Roux uzun"],
               [["RETRACTOR_ROUX_14"], ["RETRACTOR_ROUX_15"], ["RETRACTOR_ROUX_LONG"]],
               "Roux boyu cerrahın istemidir; onaylı S12-E1, S12-E2")
surgeon_choice(21, "SEPARATION_TOOL", ["Eğri Metzenbaum makas", "Monopolar koter"],
               [["SCISSORS_METZENBAUM_CURVED"], ["ESU_MONOPOLAR_PEN"]],
               "Adım adı 'makas veya monopolar koter'; doğru uygulama 'yalnızca istenen ayırma aracını uzatır'; onaylı S21-E1")
surgeon_choice(41, "HEMOSTASIS_METHOD", ["Mosquito", "Pean", "Crile düz", "Crile eğri", "Crile-Rankin", "Monopolar koter"],
               [["CLAMP_MOSQUITO"], ["CLAMP_PEAN", "CLAMP_ROCHESTER_PEAN_CURVED_14"], ["CLAMP_CRILE_STRAIGHT_14"], ["CLAMP_CRILE_CURVED_14"],
                ["CLAMP_CRILE_RANKIN_CURVED_16"], ["ESU_MONOPOLAR_PEN"]],
               "Klemp tipi ya da koter cerrahın istemidir (Pean ve Rochester-Pean aynı tiptir); onaylı S41-E1, S41-E2")
surgeon_choice(48, "PERITONEUM_EDGE_GRASP", ["2 adet Crile klemp eğri 14 cm", "Stille penset"],
               [["CLAMP_CRILE_CURVED_14"], ["FORCEPS_STILLE_NON_TOOTHED_15"]], "Hekim tercihi (v1 → v2 Adım 48)")
# Adım 49: adı "0 veya 1 Vicryl" ve varyantı iki numarayı da sayıyor; 0 Vicryl "SUPPORT" işaretliydi, kabul listesine girmiyordu.
for _i in STEPS[49]["inst"]:
    if _i["code"] == "SUTURE_POLYGLACTIN_0":
        _i["usage"] = "ALTERNATIVE"
surgeon_choice(49, "SUTURE_SIZE", ["0 Vicryl", "1 Vicryl"], [["SUTURE_POLYGLACTIN_0"], ["SUTURE_POLYGLACTIN_1"]],
               "İplik numarası her zaman cerrahça istenir; onaylı S49-E1")
surgeon_choice(50, "SUTURE_SIZE", ["0", "1"], [["SUTURE_POLYGLACTIN_0"], ["SUTURE_POLYGLACTIN_1"]],
               "İplik numarası her zaman cerrahça istenir (Q-B5); onaylı S50-E3")
surgeon_choice(51, "FASCIA_EDGE_INSTRUMENT", ["Allis 15 cm", "Allis atravmatik 19 cm", "Ochsner-Kocher", "Dişsiz penset"],
               [["CLAMP_ALLIS_15"], ["CLAMP_ALLIS_ATRAUMATIC_19"], ["CLAMP_OCHSNER_KOCHER_CURVED_20"],
                ["FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_SEMKEN_NON_TOOTHED_15_5"]],
               "Doğru uygulama 'cerrahın istemine göre'; onaylı S51-E1, S51-E2")
surgeon_choice(56, "SKIN_SUTURE", ["3/0 Prolen", "4/0 Prolen", "4/0 Polyamid", "4/0 Monocryl"],
               [["SUTURE_POLYPROPYLENE_3_0"], ["SUTURE_POLYPROPYLENE_4_0"], ["SUTURE_POLYAMIDE_4_0"], ["SUTURE_POLIGLECAPRONE_4_0"]],
               "Q-D1 hekim tercihi (kapatma tipi seçenekleri ayrıca sınırlar); onaylı S56-E2, S56-E6")
surgeon_choice(59, "DRESSING_TAPE", ["İpek flaster", "Su geçirmez flaster"], [["TAPE_SILK"], ["TAPE_WATERPROOF"]],
               "Sabitleme materyali cerrahın istemidir; onaylı S59-E1")
