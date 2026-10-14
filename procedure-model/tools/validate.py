# -*- coding: utf-8 -*-
"""Model v2 doğrulaması: referanslar, graf bütünlüğü, klinik teyit kuralları."""
import json, sys
m = json.load(open(sys.argv[1])); c = m["catalogs"]; err = []; warn = []

nodes = {s["step"]: s for s in m["steps"]}
nodes.update({x["id"]: x for sf in m["subflows"] for x in sf["steps"]})

for nid, s in nodes.items():
    if s["action"] not in c["actions"]: err.append((nid, "action", s["action"]))
    if s["target"] not in c["targets"]: err.append((nid, "target", s["target"]))
    if s.get("instrument") and s["instrument"] not in c["instruments"]: err.append((nid, "instrument", s["instrument"]))
    for i in s["instruments"]:
        if i["code"] not in c["instruments"]: err.append((nid, "inst", i["code"]))
        if i["usage"] not in c["instrumentUsage"]: err.append((nid, "usage", i["usage"]))
    for r in s["performedBy"]:
        if r not in c["roles"]: err.append((nid, "role", r))
    for x in [s["nextStep"]] + [b["goTo"] for b in s["branches"]]:
        if x is not None and x not in nodes: err.append((nid, "next", x))
    for b in s["branches"]:
        if b["when"] not in c["conditions"]: err.append((nid, "cond", b["when"]))
    for r in s.get("parameterRules", []):
        for v in r["expectedInstruments"].values():
            for code in v:
                if code not in c["instruments"]: err.append((nid, "paramInst", code))
KEYS = {"id", "title", "scrubError", "possibleComplications", "signs", "resolutionSteps", "caseContinuation", "organizationalImpact"}
for s in m["steps"]:
    for e in s["scrub"]["errorScenarios"]:
        if not KEYS <= set(e): err.append((s["step"], "senaryo alanı eksik", e["id"]))
    if not [e for e in s["scrub"]["errorScenarios"] if e["reviewStatus"] not in ("SUPERSEDED", "DRAFT")]:
        warn.append((s["step"], "aktif hata senaryosu yok (yeni senaryo bekleniyor)"))

adj = {k: [x for x in [v["nextStep"]] + [b["goTo"] for b in v["branches"]] if x is not None] for k, v in nodes.items()}
seen, st = set(), [1]
while st:
    v = st.pop()
    if v in seen: continue
    seen.add(v); st += adj[v]
if seen != set(nodes): err.append(("unreachable", set(nodes) - seen))
def reaches(v, goal=60):
    vis, st = set(), [v]
    while st:
        x = st.pop()
        if x == goal: return True
        if x in vis: continue
        vis.add(x); st += adj[x]
    return False
bad = [v for v in nodes if not reaches(v)]
if bad: err.append(("noPathTo60", bad))

# klinik teyit kuralları
s50 = nodes[50]
if any(c["instruments"][i]["category"] == "suture" and c["instruments"][i].get("absorbable") is False
       and "TEYİT" not in (x.get("note") or "") for x in s50["instruments"] for i in [x["code"]]):
    err.append((50, "non-absorbable suture accepted without pending flag"))
if "CLAMP_BABCOCK_16" in nodes[30]["acceptedInstruments"]: err.append((30, "Babcock must not be marker clamp"))
if set(nodes[8]["acceptedInstruments"]) != {"FORCEPS_STILLE_NON_TOOTHED_15", "FORCEPS_DEBAKEY", "CLAMP_CRILE_CURVED_14"}: err.append((8, "peritoneum grasp options"))
def sutures(n): return [i for i in nodes[n]["acceptedInstruments"] + [x["code"] for x in nodes[n]["instruments"]] if c["instruments"][i]["category"] == "suture"]
if set(sutures(50)) != {"SUTURE_POLYGLACTIN_0", "SUTURE_POLYGLACTIN_1"}: err.append((50, "yalnızca Vicryl 0/1", sutures(50)))
if any("POLYPROPYLENE" in x for x in sutures(52)): err.append((52, "fasyada Prolen kalmış"))
if not {"SUTURE_POLIGLECAPRONE_4_0", "SUTURE_POLYPROPYLENE_3_0", "SUTURE_POLYPROPYLENE_4_0", "SUTURE_POLYAMIDE_4_0"} <= set(sutures(56)): err.append((56, "cilt sütürü", sutures(56)))
ids = [e["id"] for s in m["steps"] for e in s["scrub"]["errorScenarios"]]
if len(ids) != len(set(ids)): err.append(("tekrarlayan senaryo kimliği", [i for i in ids if ids.count(i) > 1]))
if [e["id"] for s in m["steps"] for e in s["scrub"]["errorScenarios"] if e["reviewStatus"] == "DRAFT"]: warn.append(("onay bekleyen taslak var",))
# 52'de Prolen, 55-56'da Polyamid içerikte kalmamalı (değişiklik notları hariç). 50'de Prolen yasak kuralı ve S50-E2 hata senaryosu meşru.
for n, old in ((51, "Prolen"), (52, "Prolen")):
    # Yalnızca otomatik uyarlanan metinler + doğru uygulama; onaylı "Vicryl yerine Prolen" hata senaryoları meşru.
    body = [{k: v for k, v in e.items() if k != "review"} for e in nodes[n]["scrub"]["errorScenarios"] if e["reviewStatus"] == "ADAPTED"]
    if old in json.dumps(body + [nodes[n]["scrub"]["correctPractice"]], ensure_ascii=False): err.append((n, f"eski sütür adı ({old}) metinde kalmış"))
br43 = nodes[43]["branches"][0]
if br43.get("maxIterations") != 1: err.append((43, "kanama döngüsü sınırı"))
if not {"SCALPEL_BLADE_10", "SCALPEL_BLADE_15"} <= set(nodes[1]["acceptedInstruments"]): err.append((1, "blade 10/15"))
unres = [s["step"] for s in m["steps"] for k in s["sourceConflicts"] if k.get("status") != "RESOLVED_BY_CLINICAL_REVIEW"]
if unres: err.append(("unresolvedConflicts", unres))
assert m["preoperative"][-1]["nextStep"] == 1
for code, d in c["instruments"].items():
    if d["category"] == "suture" and "absorbable" not in d: err.append((code, "emilebilirlik bilgisi yok"))
    if d.get("absorbable") and not d.get("absorptionDays"): err.append((code, "emilme süresi yok"))
if m.get("openQuestions"): warn.append(("açık soru var", [q["id"] for q in m["openQuestions"]]))
pend = [k for k, v in nodes.items() if v.get("pendingClinicalConfirmation")]
if pend and not m.get("openQuestions"): err.append(("açık soru yok ama teyit bekleyen adım var", pend))
# Q-B2: kilolu hastada 15 cm'den uzun Farabeuf, gerekirse 15 cm'den uzun Roux; 15 cm'lik ekartörler yalnız zayıf/normal hastada.
_r4 = nodes[4].get("parameterRules", [{}])[0].get("expectedInstruments", {})
if set(_r4.get("OBESE", [])) != {"RETRACTOR_FARABEUF_LONG_SET", "RETRACTOR_ROUX_LONG"} \
        or any(set(_r4.get(b, [])) != {"RETRACTOR_FARABEUF_15_SET", "RETRACTOR_ROUX_14", "RETRACTOR_ROUX_15"} for b in ("THIN", "NORMAL")):
    err.append((4, "kilolu hasta ekartör kuralı", _r4))
# Hekim tercihi varyantları: seçenek başına alet listesi; kodlar katalogda ve adımın kabul listesinde, seçenekler ayrık olmalı.
for _n, _node in nodes.items():
    for _v in _node.get("variants", []):
        _seen = set()
        for _opt in _v.get("instruments", []):
            if not isinstance(_opt, list) or not _opt:
                err.append((_n, "varyant seçeneği alet listesi değil", _v["id"], _opt)); continue
            for _code in _opt:
                if _code not in c["instruments"] or _code not in _node.get("acceptedInstruments", []):
                    err.append((_n, "varyant aleti adımda kabul edilmiyor", _v["id"], _code))
                if _code in _seen:
                    err.append((_n, "aynı alet iki seçenekte", _v["id"], _code))
                _seen.add(_code)
print("errors:", err or "none")
print("warnings:", warn or "none")
sys.exit(1 if err else 0)
