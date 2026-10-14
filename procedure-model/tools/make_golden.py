"""Golden testler: Python akış motorunun (flow_engine.py) seçili koşul/parametre kombinasyonlarındaki sonucu.

C# backend motoru aynı girdilerle aynı yolu yürümeli ve her adımda aynı aletleri beklemelidir
(backend.Tests/FlowGoldenTests.cs). Çıktı: dist/open-appendectomy-v<sürüm>.golden.json

Çalıştırma: python3 procedure-model/tools/make_golden.py [sürüm]   (build.sh de çağırır)
"""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from flow_engine import FlowEngine  # noqa: E402

VERSION = sys.argv[1] if len(sys.argv) > 1 else "2.4"
DIST = HERE.parent / "dist"

CASES = [
    ("base", {}, {}),
    ("adhesions", {"ADHESIONS_PRESENT": True}, {}),
    ("exposure-insufficient", {"EXPOSURE_INSUFFICIENT": True}, {}),
    ("bleeding", {"BLEEDING_DETECTED": True}, {}),
    ("bleeding-after-irrigation-once", {"BLEEDING_AFTER_IRRIGATION": [True, False]}, {}),
    ("bleeding-after-irrigation-limit", {"BLEEDING_AFTER_IRRIGATION": [True, True]}, {}),
    ("ligature-not-secure", {"LIGATURE_NOT_SECURE": True}, {}),
    ("count-found-on-recount", {"COUNT_MISMATCH": True, "ITEM_FOUND_ON_RECOUNT": True}, {}),
    ("count-found-in-field", {"COUNT_MISMATCH": True, "ITEM_FOUND_IN_FIELD": True}, {}),
    ("count-found-on-imaging", {"COUNT_MISMATCH": True, "ITEM_FOUND_ON_IMAGING": True}, {}),
    ("count-not-found", {"COUNT_MISMATCH": True}, {}),
    ("obese-aesthetic", {"EXPOSURE_INSUFFICIENT": True},
     {"PATIENT_BODY_TYPE": "OBESE", "SKIN_CLOSURE_TYPE": "AESTHETIC",
      "MESO_VESSELS": [{"caliber": "SMALL"}, {"caliber": "LARGE"}]}),
    ("thin-large-vessels", {}, {"PATIENT_BODY_TYPE": "THIN",
                                "MESO_VESSELS": [{"caliber": "LARGE"}, {"caliber": "LARGE"}, {"caliber": "LARGE"}]}),
    ("everything", {"EXPOSURE_INSUFFICIENT": True, "ADHESIONS_PRESENT": True, "LIGATURE_NOT_SECURE": True,
                    "BLEEDING_DETECTED": True, "BLEEDING_AFTER_IRRIGATION": [True, True],
                    "COUNT_MISMATCH": True, "ITEM_FOUND_ON_IMAGING": True},
     {"PATIENT_BODY_TYPE": "OBESE", "SKIN_CLOSURE_TYPE": "STANDARD", "MESO_VESSELS": [{"caliber": "MEDIUM"}]}),
]


RANDOM_CASES = 400
RANDOM_SEED = 20261014
CONDITION_VALUES = [None, True, False, [True, False], [True, True], [False, True]]


def random_cases(fe):
    """Sabit tohumla rastgele koşul planı + hasta parametresi; aynı tohum hep aynı vakaları üretir."""
    import random
    rnd = random.Random(RANDOM_SEED)
    conds = [c for c, v in fe.g["conditions"].items() if v.get("source") != "OUTCOME"]
    calibers = ["SMALL", "MEDIUM", "LARGE"]
    for i in range(RANDOM_CASES):
        plan = {}
        for c in conds:
            v = rnd.choice(CONDITION_VALUES)
            if v is not None:
                plan[c] = v
        params = {
            "PATIENT_BODY_TYPE": rnd.choice(["THIN", "NORMAL", "OBESE"]),
            "SKIN_CLOSURE_TYPE": rnd.choice(["STANDARD", "AESTHETIC"]),
            "MESO_VESSELS": [{"caliber": rnd.choice(calibers)} for _ in range(rnd.randint(0, 4))],
        }
        yield f"random-{i:03d}", plan, params


def main():
    fe = FlowEngine(DIST / f"open-appendectomy-v{VERSION}.graph.json")
    out = []
    for case_id, conditions, parameters in CASES:
        r = fe.walk(conditions, parameters)
        out.append({
            "id": case_id,
            "conditions": {k: (v if isinstance(v, list) else [v]) for k, v in conditions.items()},
            "parameters": r["parameters"],
            "path": [str(x) for x in r["path"]],
            "expectedInstruments": r["expectedInstruments"],
            "stepCount": r["stepCount"],
        })
    # Rastgele vakalar: yol + yalnızca hasta parametresine bağlı düğümlerin aletleri (diğerleri sabit vakalarda denetlenir).
    dependent = {str(n["id"]) for n in fe.g["nodes"] if n.get("parameterRules") or n.get("dynamicQuantity")}
    rand = []
    for case_id, conditions, parameters in random_cases(fe):
        r = fe.walk(conditions, parameters)
        rand.append({
            "id": case_id,
            "conditions": {k: (v if isinstance(v, list) else [v]) for k, v in conditions.items()},
            "parameters": r["parameters"],
            "path": [str(x) for x in r["path"]],
            "expectedInstruments": {k: v for k, v in r["expectedInstruments"].items() if k in dependent},
        })
    rpath = DIST / f"open-appendectomy-v{VERSION}.golden-random.json"
    rpath.write_text(json.dumps({"procedureVersion": fe.g["version"], "seed": RANDOM_SEED, "cases": rand},
                                ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"golden: {len(rand)} rastgele vaka → {rpath.relative_to(HERE.parent.parent)}")

    path = DIST / f"open-appendectomy-v{VERSION}.golden.json"
    path.write_text(json.dumps({"procedureVersion": fe.g["version"], "cases": out}, ensure_ascii=False, indent=1) + "\n",
                    encoding="utf-8")
    print(f"golden: {len(out)} vaka → {path.relative_to(HERE.parent.parent)}")


if __name__ == "__main__":
    main()
