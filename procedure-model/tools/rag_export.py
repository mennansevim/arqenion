# -*- coding: utf-8 -*-
"""open-appendectomy-v2.json -> RAG çıktıları
  1) <ad>.chunks.jsonl : vektör DB'ye yüklenecek parçalar (text + metadata)
  2) <ad>.graph.json   : akış motoru için kompakt graf (embed EDİLMEZ)
Kullanım: python3 rag_export.py open-appendectomy-v2.json

Kurallar:
  - reviewStatus=SUPERSEDED olan hata senaryoları RAG'a girmez (klinik teyitle çelişiyor).
  - CONDITIONAL ve PENDING senaryolar girer, metinde ve metadata'da durumları yazar.
  - Açık sorular (openQuestions) bilgi değildir, RAG'a girmez.
"""
import json, sys, os
from collections import Counter

SRC = sys.argv[1]
base = os.path.splitext(SRC)[0]
m = json.load(open(SRC))
C = m["catalogs"]
VER = m["procedure"]["version"]
PROC = m["procedure"]["id"]
phase_name = {p["id"]: p["name"] for p in m["phases"]}


def inst_name(code): return C["instruments"][code]["name"]
def role_names(rs): return ", ".join(C["roles"][r] for r in rs)
def cdesc(code): return C["conditions"][code]["description"]


def bl(items, prefix="- "):
    out = []
    for it in items:
        if isinstance(it, dict):
            if it.get("category"): out.append(f"{prefix}{it['category']}:")
            out += [f"  {prefix}{x}" for x in it["items"]]
        else:
            out.append(f"{prefix}{it}")
    return "\n".join(out)


chunks = []
def add(cid, ctype, text, **meta):
    md = {"procedure": PROC, "version": VER, "chunkType": ctype}
    md.update({k: v for k, v in meta.items() if v is not None})
    chunks.append({"id": f"{PROC}@{VER}:{cid}", "text": text.strip(), "metadata": md})


def inst_lines(insts):
    L = []
    for i in insts:
        q = f" x{i['qty']}" if i["qty"] > 1 else ""
        n = f" — {i['note']}" if i.get("note") else ""
        L.append(f"- {inst_name(i['code'])}{q} ({C['instrumentUsage'][i['usage']]}){n}")
    return L


def step_extras(s, L):
    for v in s.get("variants", []):
        L.append(f"Seçenek ({v['id']}): {' / '.join(v['options'])} — karar: {C['roles'][v['decidedBy']]}")
    for r in s.get("rules", []):
        L.append(f"Kural {r['id']}: {r['text']}")
    for r in s.get("parameterRules", []):
        m_ = "; ".join(f"{k}: {', '.join(inst_name(x) for x in v)}" for k, v in r["expectedInstruments"].items())
        L.append(f"Hasta parametresine göre alet ({r['parameter']}): {m_}. {r.get('note', '')}")
    dq = s.get("dynamicQuantity")
    if dq:
        cal = ", ".join(f"{k.lower()} damar → {inst_name(v)}" for k, v in dq["instrumentByCaliber"].items())
        L.append(f"Klemp adedi ve tipi ({dq['parameter']}): {dq['rule']}; {cal}.")
    sr = s.get("selectionRule")
    if sr:
        L.append("Hemostatik klemp seçimi damar çapına göre: " + ", ".join(f"{k.lower()} → {inst_name(v)}" for k, v in sr["instrumentByCaliber"].items()) + ".")
    for o in s.get("adaptiveOutcomes", []):
        L.append(f"Adaptif sonuç: {o['onScenario']} gerçekleşirse '{cdesc(o['condition'])}' olup olmayacağını adaptif motor dijital ikize göre seçer. Etki: {o['effect']}")
    if s.get("adaptiveCondition"):
        L.append(f"Adaptif koşul: {cdesc(s['adaptiveCondition'])} — gerçekleşip gerçekleşmeyeceğini adaptif motor seçer.")
    d = s.get("instrumentDisposition")
    if d:
        L.append(f"Alet akıbeti: {inst_name(d['code'])} → {d['location']} ({C['roles'][d['handledBy']]}), kapanış sayımında sayılır.")
    for k in ("preconditions", "notes"):
        for x in s.get(k, []):
            L.append(f"{'Ön koşul' if k == 'preconditions' else 'Not'}: {x}")
    if s.get("pendingClinicalConfirmation"):
        L.append("Teyit bekleyen konu var: " + ", ".join(s["pendingClinicalConfirmation"]))


# ---- Adım chunk'ları ----
for s in m["steps"]:
    ph = phase_name[s["phaseId"]]
    L = [f"Açık apendektomi — Adım {s['step']}: {s['name']}",
         f"Faz: {ph} ({s['phaseId']})",
         f"İşlem: {C['actions'][s['action']]} [{s['action']}]",
         f"Hedef: {C['targets'][s['target']]} [{s['target']}]",
         f"Uygulayan: {role_names(s['performedBy'])}",
         f"Zorunlu: {'Evet' if s['isMandatory'] else 'Hayır'}"]
    if s["isConditional"]:
        c = s["condition"]
        L.append(f"Koşullu adım: yalnızca şu durumda uygulanır → {c['description']} [{c['code']}], kaynak: {c.get('source')}")
    if s["branches"]:
        L.append("Karar noktası:" + "".join(f"\n- {cdesc(b['when'])} ise → {b['goTo']}" for b in s["branches"]) + f"\n- Aksi halde → Adım {s['nextStep']}")
    else:
        L.append(f"Sonraki adım: {s['nextStep'] if s['nextStep'] else 'Yok (operasyon sonu)'}")
    hv = s["newInstrumentHandover"]
    L.append(f"Bu adımda yeni alet uzatılır mı: {'Hasta parametresine bağlı' if hv == 'DEPENDS_ON_PARAMETER' else 'Evet' if hv else 'Hayır'}")
    if s["instruments"]:
        L.append("Aletler:")
        L += inst_lines(s["instruments"])
    step_extras(s, L)
    L.append("Doğru scrub uygulaması: " + s["scrub"]["correctPractice"])
    act = [e for e in s["scrub"]["errorScenarios"] if e["reviewStatus"] not in ("SUPERSEDED", "DRAFT")]
    if act:
        L.append("Bu adımdaki hata senaryoları: " + "; ".join(f"{e['id']} {e['title']}" for e in act))
    for r in s["clinicalReview"]:
        L.append(f"Klinik teyit (soru {r['question']}): {r['decision']}" + (f" — {r['note']}" if r.get("note") else ""))
    add(f"step:{s['step']}", "step", "\n".join(L),
        step=s["step"], phaseId=s["phaseId"], phase=ph, action=s["action"], target=s["target"],
        instruments=[i["code"] for i in s["instruments"]], primaryInstrument=s["instrument"],
        roles=s["performedBy"], isMandatory=s["isMandatory"], isConditional=s["isConditional"],
        condition=s["condition"]["code"] if s["condition"] else None, isDecisionPoint=s["isDecisionPoint"],
        clinicallyReviewed=bool(s["clinicalReview"]), hasPending=bool(s.get("pendingClinicalConfirmation")))

    # ---- Hata senaryosu chunk'ları ----
    for e in act:
        sg = e.get("signs", [])
        sg_txt = ("\n".join(f"{'Ameliyat sırasında' if k == 'intraoperative' else 'Ameliyat sonrasında'}:\n{bl(v)}" for k, v in sg.items())
                  if isinstance(sg, dict) else bl(sg))
        T = [f"Açık apendektomi — Adım {s['step']} ({s['name']}) — Hata senaryosu {e['id']}: {e['title']}", f"Faz: {ph}"]
        if e["reviewStatus"] == "CONDITIONAL":
            T.append(f"Geçerlilik: yalnızca {e['review']['applicableWhen']}.")
        if e["reviewStatus"] == "PENDING":
            T.append(f"Durum: klinik teyit bekliyor — {e['review']['note']}")
        T += [f"Scrub'ın hatası: {e.get('scrubError', '')}",
              "Olası komplikasyonlar:\n" + bl(e.get("possibleComplications", [])),
              "Belirtiler:\n" + sg_txt,
              "Scrub'ın sıralı çözüm yaklaşımı:\n" + "\n".join(f"{k + 1}. {x}" for k, x in enumerate(e.get("resolutionSteps", []))),
              f"Vakanın devamı: {e.get('caseContinuation', '')}",
              "Yönetim ve organizasyona etkisi:\n" + bl(e.get("organizationalImpact", []))]
        add(f"error:{e['id']}", "error_scenario", "\n".join(T),
            step=s["step"], phaseId=s["phaseId"], phase=ph, scenarioId=e["id"], reviewStatus=e["reviewStatus"],
            instruments=[i["code"] for i in s["instruments"]], action=s["action"])

# ---- Alt akış chunk'ları ----
for sf in m["subflows"]:
    for x in sf["steps"]:
        L = [f"Açık apendektomi — Alt akış '{sf['name']}' — {x['id']}: {x['name']}",
             f"Giriş: Adım {sf['entryFrom']} · Uygulayan: {role_names(x['performedBy'])}",
             f"İşlem: {C['actions'][x['action']]} [{x['action']}] · Hedef: {C['targets'][x['target']]}"]
        if x["branches"]:
            L.append("Karar:" + "".join(f"\n- {cdesc(b['when'])} ise → {b['goTo']}" for b in x["branches"]) + f"\n- Aksi halde → {x['nextStep']}")
        else:
            L.append(f"Sonraki: {x['nextStep']}")
        if x["instruments"]:
            L.append("Aletler:")
            L += inst_lines(x["instruments"])
        for n_ in x.get("notes", []):
            L.append(f"Not: {n_}")
        if x.get("pendingClinicalConfirmation"):
            L.append("Teyit bekleyen konu var: " + ", ".join(x["pendingClinicalConfirmation"]))
        add(f"subflow:{x['id']}", "subflow_step", "\n".join(L), subflowId=sf["id"], node=x["id"],
            step=int(''.join(ch for ch in x["id"] if ch.isdigit())), roles=x["performedBy"])

# ---- Ameliyat öncesi ----
for p in m["preoperative"]:
    L = [f"Açık apendektomi — Ameliyat öncesi {p['id']}: {p['name']}",
         f"Uygulayan: {role_names(p['performedBy'])}",
         f"Sonraki: {'Adım ' + str(p['nextStep']) if isinstance(p['nextStep'], int) else p['nextStep']}"]
    if p.get("tasks"): L.append("Yapılacaklar:\n" + bl(p["tasks"]))
    if p.get("responsibilityNote"): L.append("Sorumluluk: " + p["responsibilityNote"])
    for a in p.get("alternatives", []):
        L.append(f"{a['name']} ({a['id']}):\n" + bl(a["tasks"]))
    if p.get("selection"): L.append(p["selection"])
    if p.get("omissionRisks"): L.append("Uygun yapılmazsa:\n" + bl(p["omissionRisks"]))
    for n_ in p.get("notes", []): L.append(f"Not: {n_}")
    for sp in p.get("substeps", []):
        L.append(f"{sp['id']} {sp['name']}" + (f": {sp['task']}" if sp.get("task") else ""))
        if sp.get("isConditional"): L.append(f"  (Koşullu: {sp['condition']})")
        if sp.get("omissionRisks"): L.append("  Yapılmazsa:\n" + bl(sp["omissionRisks"], "  - "))
        for f in sp.get("forms", []):
            L.append(f"  Form: {f['form']} — kontrol edilmezse:")
            for r in f["omissionRisks"]:
                if isinstance(r, dict):
                    L.append(f"    * {r['item']}:\n" + bl(r["complications"], "      - "))
                else:
                    L.append(f"    - {r}")
    add(f"preop:{p['id']}", "preoperative", "\n".join(L), preopId=p["id"], roles=p["performedBy"])
    for k, ec in enumerate(p.get("errorChecklist", [])):
        T = [f"Açık apendektomi — Ameliyat öncesi {p['id']} ({p['name']}) — Hata kontrol listesi: {ec['task']}"]
        for er in ec["errors"]:
            T.append(f"- Hata: {er['error']}\n" + bl(er["consequences"], "    • "))
        add(f"preop:{p['id']}:check:{k + 1}", "preop_error_checklist", "\n".join(T), preopId=p["id"], roles=p["performedBy"])

# ---- Genel kurallar, koşullar, alet kataloğu ----
add("rules", "global_rules", "Açık apendektomi — Genel kurallar:\n" + "\n".join(f"- {r['id']}: {r['text']}" for r in m["globalRules"]))
add("conditions", "conditions", "Açık apendektomi — Akış koşulları ve kaynakları:\n" + "\n".join(
    f"- {c} ({v.get('source')}): {v['description']}" + (f" · değerlendirildiği yer: {v['evaluatedAt']}" if v.get("evaluatedAt") else "")
    for c, v in C["conditions"].items()) + "\n" + m["procedure"]["decisionPolicy"])
usage = {}
for s in m["steps"]:
    for i in s["instruments"]: usage.setdefault(i["code"], []).append(s["step"])
for code, d in C["instruments"].items():
    st = sorted(set(usage.get(code, [])))
    extra = []
    if d.get("absorbable") is not None:
        ad = d.get("absorptionDays")
        extra.append(f"Materyal: {d.get('material', '—')} · {'emilebilir' if d['absorbable'] else 'emilmez'}"
                     + (f" · emilme süresi {ad['text']} gün" if ad else "")
                     + (f" · {'tek telli' if d.get('structure') == 'MONOFILAMENT' else 'örgülü'}" if d.get("structure") else ""))
    if d.get("absorbableNote"): extra.append(f"Not: {d['absorbableNote']}")
    if "vesselCaliber" in d: extra.append(f"Damar çapı: {d['vesselCaliber'].lower()}")
    if "inSet" in d: extra.append(f"Apendektomi setinde: {'evet' if d['inSet'] else 'hayır, haricen açılan sarf'}" + (f" ({d['note']})" if d.get("note") else ""))
    add(f"instrument:{code}", "instrument",
        f"Alet: {d['name']} [{code}]\nKategori: {d['category']}\n" + "\n".join(extra) + f"\nKullanıldığı adımlar: {', '.join(map(str, st)) or '—'}",
        instrument=code, steps=st)

ref = C.get("sutureReference")
if ref:
    for e in ref["sutures"]:
        days = e["absorptionDays"]["text"] + " gün" if e["absorptionDays"] else "emilmez"
        code = [k for k, v in C["instruments"].items() if v.get("referenceName") == e["name"]]
        add(f"suture-ref:{e['name']}", "suture_reference",
            f"Cerrahi sütür: {e['name']}\nMateryal: {e['material']}\nSınıf: {'emilebilen' if e['absorbable'] else 'emilemeyen'}, "
            f"{'tek telli (monofilament)' if e['structure'] == 'MONOFILAMENT' else 'örgülü (multifilament)'}\nEmilme süresi: {days}"
            + (f"\nBu prosedürde katalog kodları: {', '.join(code)}" if code else "\nBu prosedürün akışında kullanılmıyor")
            + f"\nKaynak: {ref['source']}",
            suture=e["name"], absorbable=e["absorbable"], structure=e["structure"], instruments=code or None)

with open(base + ".chunks.jsonl", "w") as f:
    for c in chunks: f.write(json.dumps(c, ensure_ascii=False) + "\n")

# ---- Kompakt graf ----
def node_of(s, kind):
    return {"id": s.get("step", s.get("id")) if kind != "subflow" else s["id"], "kind": kind,
            "isDecision": bool(s.get("isDecisionPoint")), "label": s["name"],
            "phaseId": s.get("phaseId"), "subflowId": s.get("subflowId"), "action": s["action"],
            "instrument": s.get("instrument"), "target": s["target"], "roles": s["performedBy"],
            "mandatory": s.get("isMandatory", True), "condition": (s.get("condition") or {}).get("code"),
            "accepted": s.get("acceptedInstruments", []), "parameterRules": s.get("parameterRules", []),
            "dynamicQuantity": s.get("dynamicQuantity"),
            "pending": [i["code"] for i in s["instruments"] if "TEYİT BEKLİYOR" in (i.get("note") or "")],
            "next": s["nextStep"], "branches": s["branches"]}

graph = {
    "procedure": PROC, "version": VER,
    "conditions": C["conditions"],
    "parameters": C["parameters"],
    "parameterDefaults": {k: v["default"] for k, v in C["parameters"].items()},
    "entry": m["preoperative"][0]["id"],
    "nodes": [{"id": p["id"], "kind": "preop", "isDecision": False, "label": p["name"], "next": p["nextStep"], "branches": []} for p in m["preoperative"]]
           + [node_of(s, "conditional" if s["isConditional"] else "step") for s in m["steps"]]
           + [node_of(x, "subflow") for sf in m["subflows"] for x in sf["steps"]],
    "phases": [{"id": p["id"], "name": p["name"], "steps": p["steps"]} for p in m["phases"]],
    "subflows": [{"id": sf["id"], "name": sf["name"], "entryFrom": sf["entryFrom"], "steps": [x["id"] for x in sf["steps"]]} for sf in m["subflows"]],
}
json.dump(graph, open(base + ".graph.json", "w"), ensure_ascii=False, indent=1)

print("chunks:", len(chunks), dict(Counter(c["metadata"]["chunkType"] for c in chunks)))
L = [len(c["text"]) for c in chunks]
print("chars min/avg/max:", min(L), sum(L) // len(L), max(L))
