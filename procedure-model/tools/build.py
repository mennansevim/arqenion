# -*- coding: utf-8 -*-
"""open-appendectomy-v1.json derleyicisi.
Kullanım: python3 build.py <arqenion.excalidraw> <apendektomi.pdf> <cikti.json>
"""
import json, re, sys, subprocess, hashlib, datetime
from clinical_review import *

EXC, PDF, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
exc = json.load(open(EXC))
TXT = {e['id']: e['text'] for e in exc['elements'] if e['type'] == 'text'}

def T(prefix):
    m = [v for k, v in TXT.items() if k.startswith(prefix)]
    assert len(m) == 1, prefix
    return m[0]

def clean(s):
    return re.sub(r'\s+', ' ', s).strip()

MARK = re.compile(r'^\s*(?:\d+\.|•|○|\*|-)\s*')

def bullet_list(txt, drop_header=False):
    lines = [l for l in txt.split('\n') if l.strip()]
    if drop_header: lines = lines[1:]
    out = []
    for l in lines:
        s = MARK.sub('', l).strip()
        if not s: continue
        if out and not MARK.match(l) and not out[-1].endswith(('.', ':', ')')):
            out[-1] += ' ' + s
        else:
            out.append(s)
    return out

def error_list(txt):
    """'N. Başlık / • hata / ○ sonuç' biçimli bloklar."""
    lines = [l for l in txt.split('\n') if l.strip()]
    title = MARK.sub('', lines[0]).strip()
    errs = []
    for l in lines[1:]:
        st = l.strip()
        if st.startswith('•'):
            errs.append({"error": st[1:].strip(), "consequences": []})
        elif st.startswith('○'):
            errs[-1]["consequences"].append(st[1:].strip())
        else:  # devam satırı ya da işaretsiz son sonuç
            tgt = errs[-1]["consequences"] if errs[-1]["consequences"] else None
            if tgt is not None and not tgt[-1].endswith('.'):
                tgt[-1] += ' ' + st
            elif tgt is not None:
                tgt.append(st)
            else:
                errs[-1]["error"] += ' ' + st
    return title, errs

# ---------------- Ameliyat öncesi akış ----------------
def comp(text_id, header=True):
    return bullet_list(T(text_id), drop_header=header)

def group_caps(items):
    out, cur = [], None
    for it in items:
        if it.isupper() and len(it) > 6:
            cur = {"category": it, "items": []}; out.append(cur)
        elif cur is None:
            cur = {"category": None, "items": [it]}; out.append(cur)
        else:
            cur["items"].append(it)
    return out if any(g["category"] for g in out) else items

anest_detail = [group_caps(comp(i, header=True)) for i in ["Qamfy_X3", "JjRk2ST4", "MJMlCNa_", "lNi9-EEP", "JS-gM3OS", "kVS4YzjL"]]
anam_detail = [comp(i, header=True) for i in ["8aNsb99h", "jnFYQptP", "Yt0uGR8d", "V-9tJQzL", "bh0Gd7dd", "uapVXTJ1", "B1SUF71G"]]
anest_items = ["Anestezi yöntemi bilinemez", "Açlık süresi bilinemez", "Normal vital bulgular bilinemez", "Hava yolu değerlendirilmesi bilinemez", "Laboratuvar ve radyoloji bulguları bilinemez", "ASA risk değerlendirmesi yapılamaz"]
anam_items = ["Geçirilmiş ameliyatlar bilinemez", "Anestezi öyküsü bilinemez", "Geçmiş anestezi komplikasyonları bilinemez", "Kalıtsal rahatsızlıklar bilinemez", "Düzenli kullanılan ilaçlar bilinemez", "Alerjiler bilinemez", "Alışkanlıklar (sigara, alkol, madde) bilinemez"]

def sub(id_, name, by, items=None, **kw):
    d = {"id": id_, "name": name, "performedBy": by}
    if items is not None: d["tasks"] = items
    d.update(kw); return d

PREOP = [
    sub("PRE01", "Hasta ve ameliyat doğrulaması", ["TEAM"], substeps=[
        {"id": "PRE01.1", "name": "Hasta kimliğinin doğrulanması", "task": clean(T("oDNWEGTz")),
         "omissionRisks": ["Hastaya yanlış ameliyatın uygulanması"]},
        {"id": "PRE01.2", "name": "Ameliyat adının teyidi", "task": "Hastaya hangi ameliyatı olacağı sorularak dosyadaki ameliyat adı ile teyit edilir.",
         "omissionRisks": ["Hastaya yanlış ameliyatın uygulanması"]},
        {"id": "PRE01.3", "name": "Taraf işaretlemesinin kontrolü", "task": "Taraf cerrahisi olacak ise taraf işaretlemesinin yapılıp yapılmadığı kontrol edilir (böbrek, bacak, kol vb. vücutta birden fazla olan yapılar).",
         "isConditional": True, "condition": "Taraf cerrahisi", "omissionRisks": comp("GjVHsKRl")},
        {"id": "PRE01.4", "name": "Hasta formlarının kontrolü", "task": "Cerrahi onam, anestezi onam, hasta anamnez ve bilgi, anestezi muayene formları kontrol edilir.",
         "forms": [
            {"form": "Cerrahi onam formu", "omissionRisks": comp("adL1btBX")},
            {"form": "Anestezi onam formu", "omissionRisks": comp("ZEibGJkO")},
            {"form": "Anestezi muayene formu", "omissionRisks": [{"item": it, "complications": c} for it, c in zip(anest_items, anest_detail)]},
            {"form": "Hasta anamnez ve bilgi formu", "omissionRisks": [{"item": it, "complications": c} for it, c in zip(anam_items, anam_detail)]},
         ]},
    ]),
    sub("PRE02", "Hastanın ameliyat odasına alınması ve genel anestezi", ["ANESTHESIA", "SCRUB"], bullet_list(T("jJVKhx2v")),
        responsibilityNote=clean(T("frP0BOvh"))),
    sub("PRE03", "Monopolar elektrokoter hazırlığı", ["CIRCULATOR"], bullet_list(T("zNdJB0T5"), drop_header=True),
        errorChecklist=[dict(zip(("task", "errors"), error_list(T(i)))) for i in ["4yNJjycy", "AoIfBd7_", "YQJq7Ar5", "mDr9iNjy"]]),
    sub("PRE04", "Hastanın antiseptik solüsyon ile boyanması", ["SCRUB", "CIRCULATOR"], ["Hasta povidon iyot veya klorheksidin ile boyanır."],
        alternatives=[{"id": "TECHNIQUE_1", "name": "Dairesel teknik", "tasks": bullet_list(T("pgXXQQEB"), drop_header=True)},
                      {"id": "TECHNIQUE_2", "name": "Yukarıdan aşağı çizgisel teknik", "tasks": bullet_list(T("Tq3AITAD"), drop_header=True)[:-1]}],
        selection="Tekniklerden yalnızca biri seçilir", instruments=["CLAMP_FOERSTER_CURVED_24", "STERILE_GAUZE_SPONGE", "POVIDONE_IODINE"],
        omissionRisks=bullet_list(T("pT98dy7-"), drop_header=True)),
    sub("PRE05", "Hastanın steril örtülmesi", ["SCRUB", "CIRCULATOR"], bullet_list(T("vI_cnv01"), drop_header=True)),
    sub("PRE06", "Mayo masasının düzenlenmesi", ["SCRUB"], bullet_list(T("wHJjXyHh"), drop_header=True),
        omissionRisks=bullet_list(T("Gx13XN9b"), drop_header=True)),
    sub("PRE07", "Koter kalemi ve aspiratör hortumunun steril alana açılması", ["CIRCULATOR"], bullet_list(T("gzx8Zyxx"), drop_header=True),
        errorChecklist=[dict(zip(("task", "errors"), error_list(T(i)))) for i in ["akArYwPL", "aULaqnS2", "Y4d6ido3"]]),
    sub("PRE08", "Koter ve aspiratör bağlantılarının kurulması", ["SCRUB", "CIRCULATOR"], bullet_list(T("QMibyAB_"), drop_header=True),
        errorChecklist=[dict(zip(("task", "errors"), error_list(T(i)))) for i in ["2pLzOnw8", "ITjFwzJW", "eGq8Uonc", "sK9tOAjU", "BN9d9BQT", "KseFQ7Bs", "SfMsy5P8"]]),
    sub("PRE09", "Başlangıç sayımı", ["SCRUB", "CIRCULATOR"], substeps=[
        {"id": "PRE09.1", "name": "Spanç sayımı", "omissionRisks": bullet_list(T("6FtH57L1"))},
        {"id": "PRE09.2", "name": "Kompres sayımı", "omissionRisks": bullet_list(T("pF_0CmM0"))},
        {"id": "PRE09.3", "name": "Alet sayımı", "omissionRisks": bullet_list(T("_-1G99MS"))},
        {"id": "PRE09.4", "name": "İğne sayımı", "omissionRisks": bullet_list(T("pxXXvhah"))},
        {"id": "PRE09.5", "name": "Sayımın doğrulanması", "omissionRisks": bullet_list(T("5er1COSh"))},
    ]),
]
# PRE09.1 bloğunda tekrar eden bir madde var; tekilleştir
for s in PREOP[-1]["substeps"]:
    s["omissionRisks"] = list(dict.fromkeys(s["omissionRisks"]))
for i, p in enumerate(PREOP):
    p["order"] = i + 1
    p["nextStep"] = PREOP[i + 1]["id"] if i + 1 < len(PREOP) else 1
    if p["id"] in PREOP_NOTES:
        p.setdefault("notes", []).append(PREOP_NOTES[p["id"]])

# ---------------- Kaynak metinleri ----------------
pdf_txt = subprocess.run(["pdftotext", "-layout", PDF, "-"], capture_output=True, text=True).stdout
pdf_txt = pdf_txt.replace('\u200b', '')
part1, part2 = pdf_txt.split("Her Adımda Kullanılan Alet")
pdf_step = {int(m.group(1)): clean(m.group(2)) for m in re.finditer(r'Adım\s*(\d+):\s*(.+)', part1)}
part2 = part2.split("Notlar")[0]
pdf_inst = {}
for m in re.finditer(r'(\d+)\.Adım:\s*(.*?)(?=\n\s*\d+\.Adım:|\Z)', part2, flags=re.S):
    pdf_inst[int(m.group(1))] = clean(m.group(2))
assert len(pdf_step) == 60 and len(pdf_inst) == 60, (len(pdf_step), len(pdf_inst))

exc_mid = {}
for e in exc['elements']:
    if e['type'] != 'text' or e['x'] < 36000 or 'Doğru scrub' in e['text'] or 'Adım' not in e['text']: continue
    parts = re.split(r'(?:^|\n)\s*(?:\d+\.\s*)?Adım\s*(\d+)\s*[:–-]\s*', '\n' + e['text'])
    for i in range(1, len(parts), 2):
        exc_mid[int(parts[i])] = parts[i + 1]
assert len(exc_mid) == 60, sorted(set(range(1, 61)) - set(exc_mid))

# ---------------- Detaylar (doğru scrub uygulaması + hata senaryoları) ----------------
import os as _os
_HERE = _os.path.dirname(_os.path.abspath(__file__)); _DET = _os.path.join(_HERE, "details.json")
subprocess.run([sys.executable, _os.path.join(_HERE, "parse_excalidraw.py"), EXC, _DET], check=True, capture_output=True)
details = {int(k): v for k, v in json.load(open(_DET)).items()}

# ---------------- Adımlar ----------------
phase_of = {n: (pid, pname) for pid, pname, rng in PHASES for n in rng}

def cond_obj(code):
    meta = CONDITION_META.get(code, {})
    return {"code": code, "description": CONDITIONS[code], **meta}

def _deep_replace(o, pairs):
    if isinstance(o, str):
        for a, b in pairs:
            o = o.replace(a, b)
        return o
    if isinstance(o, list):
        return [_deep_replace(x, pairs) for x in o]
    if isinstance(o, dict):
        return {k: (_deep_replace(v, pairs) if k != "id" else v) for k, v in o.items()}
    return o

def flag_scenarios(scens):
    out = []
    for e in scens:
        e = dict(e)
        f = SCENARIO_FLAGS.get(e["id"])
        if f and f.get("replace"):
            e = _deep_replace(e, f["replace"])
        e["reviewStatus"] = f["status"] if f else "ACTIVE"
        if f:
            e["review"] = {k: v for k, v in f.items() if k not in ("status", "replace")}
        out.append(e)
    return out

steps = []
for n in range(1, 61):
    s = STEPS[n]
    primary = next((i["code"] for i in s["inst"] if i["usage"] == "PRIMARY"), None)
    if primary is None:
        primary = next((i["code"] for i in s["inst"] if i["usage"] in ("ALTERNATIVE", "IN_USE")), None)
    cond = s.get("condition")
    handover = s.get("newInstrument", any(i["usage"] in ("PRIMARY", "SUPPORT", "ALTERNATIVE") for i in s["inst"]))
    st = {
        "step": n,
        "id": f"S{n:02d}",
        "phaseId": phase_of[n][0],
        "name": s["name"],
        "action": s["action"],
        "instrument": primary,
        "target": s["target"],
        "nextStep": NEXT_OVERRIDE.get(n, n + 1),
        "isMandatory": s.get("mandatory", True),
        "isConditional": cond is not None,
        "condition": cond_obj(cond) if cond else None,
        "isDecisionPoint": bool(s.get("decision") or n in BRANCHES),
        "branches": BRANCHES.get(n, []),
        "performedBy": s["by"],
        "newInstrumentHandover": handover,
        "acceptedInstruments": [i["code"] for i in s["inst"] if i["usage"] in ("PRIMARY", "ALTERNATIVE")],
        "instruments": s["inst"],
    }
    for k in ("variants", "energyMode", "sequenceMatters", "preconditions", "countUpdate", "technique", "recordFields", "notes",
              "rules", "parameterRules", "dynamicQuantity", "selectionRule", "adaptiveOutcomes", "adaptiveCondition",
              "instrumentDisposition", "pendingClinicalConfirmation"):
        if k in s: st[k] = s[k]
    st["clinicalReview"] = s.get("review", [])
    st["sourceConflicts"] = s.get("conflicts", [])
    d = details[n]
    practice = PRACTICE_OVERRIDES.get(n)
    if not practice and n in PRACTICE_REPLACE:
        practice = _deep_replace(d["correctScrubPractice"], PRACTICE_REPLACE[n])
    drafts = []
    for x in DRAFT_SCENARIOS.get(n, []) if "DRAFT_SCENARIOS" in globals() else []:
        x = dict(x); meta = x.pop("_draft")
        appr = (APPROVED_DRAFTS if "APPROVED_DRAFTS" in globals() else {}).get(x["id"])
        if appr:
            x["review"] = {"formerId": x["id"], "decision": appr["decision"], **APPROVAL_META,
                           **({"replaces": meta["replaces"]} if meta.get("replaces") else {})}
            x["id"] = appr["newId"]
            x["reviewStatus"] = "ACTIVE"
        else:
            x["reviewStatus"] = "DRAFT"
            x["review"] = {"note": "Klinik onay bekliyor (Q-C1)", **{k: v for k, v in meta.items() if v}}
        drafts.append(x)
    st["scrub"] = {"correctPractice": practice or d["correctScrubPractice"],
                   "errorScenarios": flag_scenarios(d["errorScenarios"]) + drafts}
    if practice:
        st["scrub"]["correctPracticeSource"] = "CLINICAL_REVIEW"
        st["scrub"]["correctPracticeExcalidraw"] = d["correctScrubPractice"]
    st["sources"] = {"pdf": {"step": pdf_step[n], "instruments": pdf_inst[n]},
                     "excalidraw": {"step": clean(exc_mid[n]), "detailTitle": d["detailTitle"]}}
    steps.append(st)

subflows = []
for sf in SUBFLOWS:
    sf = json.loads(json.dumps(sf))
    for x in sf["steps"]:
        x["subflowId"] = sf["id"]
        x.setdefault("isMandatory", True)
        x["isConditional"] = True
        x["isDecisionPoint"] = bool(x["branches"])
        x["acceptedInstruments"] = [i["code"] for i in x["instruments"] if i["usage"] in ("PRIMARY", "ALTERNATIVE")]
    subflows.append(sf)

model = {
    "schemaVersion": "2.4.0",
    "procedure": {
        "id": "open-appendectomy",
        "name": "Açık Apendektomi",
        "version": VERSION,
        "fileName": FILE_NAME,
        "previousVersion": "2.2.0",
        "language": "tr",
        "createdAt": datetime.date.today().isoformat(),
        "intendedUse": "Denetimli klinik simülasyon ve sağlık profesyonellerinin eğitimi (kaynak metinlerdeki kullanım notu)",
        "entryPoint": PREOP[0]["id"],
        "surgicalEntryStep": 1,
        "terminalStep": 60,
        "sources": [
            {"id": "excalidraw", "file": "arqenion.excalidraw", "role": "primary", "sha256": hashlib.sha256(open(EXC, 'rb').read()).hexdigest()},
            {"id": "pdf", "file": "Apendektomi_ameliyat_.docx.pdf", "role": "secondary", "sha256": hashlib.sha256(open(PDF, 'rb').read()).hexdigest()},
            {"id": "clinical-review", **REVIEW_SOURCE, "role": "override"},
        ],
        "conflictPolicy": "Klinik teyit (clinical-review) her iki kaynağın önündedir. Teyit edilmemiş alanlarda Excalidraw esas alınır; PDF değeri sourceConflicts altında saklanır.",
        "decisionPolicy": "Doğru/yanlış kararını deterministik motor verir. source=ADAPTIVE koşulların gerçekleşip gerçekleşmeyeceğini (kanama, enfeksiyon, eksik malzemenin yeri) Adaptive Training Engine dijital ikize göre seçer; AI bu seçime öneri verebilir, seçim izin verilen değerlerle sınırlıdır ve kaydedilir.",
    },
    "catalogs": {
        "actions": ACTIONS,
        "targets": TARGETS,
        "instruments": INSTRUMENTS,
        "roles": ROLES,
        "instrumentUsage": USAGE,
        "conditions": {c: cond_obj(c) for c in CONDITIONS},
        "parameters": PARAMETERS,
        "hemostatByCaliber": HEMOSTAT_BY_CALIBER,
        "sutureReference": SUTURE_REFERENCE,
    },
    "globalRules": GLOBAL_RULES,
    "preoperative": PREOP,
    "phases": [{"id": pid, "order": i + 1, "name": pname, "steps": list(rng)} for i, (pid, pname, rng) in enumerate(PHASES)],
    "steps": steps,
    "subflows": subflows,
    "openQuestions": OPEN_QUESTIONS,
    "resolvedQuestions": [{**q, **DECISION_META} for q in RESOLVED_QUESTIONS],
}
allsc = [e for s in steps for e in s["scrub"]["errorScenarios"]]
from collections import Counter
model["summary"] = {
    "stepCount": len(steps),
    "subflowStepCount": sum(len(sf["steps"]) for sf in subflows),
    "preoperativeStepCount": len(PREOP),
    "optionalSteps": [s["step"] for s in steps if not s["isMandatory"]],
    "conditionalSteps": [s["step"] for s in steps if s["isConditional"]],
    "decisionPoints": [s["step"] for s in steps if s["isDecisionPoint"]] + [x["id"] for sf in subflows for x in sf["steps"] if x["isDecisionPoint"]],
    "adaptiveConditions": [c for c, m in CONDITION_META.items() if m.get("source") == "ADAPTIVE"],
    "stepsWithClinicalReview": [s["step"] for s in steps if s["clinicalReview"]],
    "unresolvedSourceConflicts": [s["step"] for s in steps for c in s["sourceConflicts"] if c.get("status") != "RESOLVED_BY_CLINICAL_REVIEW"],
    "errorScenarioCount": len(allsc),
    "errorScenarioStatus": dict(Counter(e["reviewStatus"] for e in allsc)),
    "openQuestions": [q["id"] for q in OPEN_QUESTIONS],
}
json.dump(model, open(OUT, "w"), ensure_ascii=False, indent=2)
print(json.dumps(model["summary"], ensure_ascii=False))
