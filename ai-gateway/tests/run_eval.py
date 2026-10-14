"""Test seti: modeldeki her aktif hata senaryosu için bir sapma + senaryo eşleştirme vakaları.

Kullanım:
  python tests/run_eval.py                 # .env'deki yazıcıyla (anahtar yoksa hepsi hazır metne düşer)
  python tests/run_eval.py --fake good     # sahte "iyi" yazıcıyla akışın tamamını dener
  python tests/run_eval.py --limit 40      # ilk 40 senaryo (gerçek yapay zekâda maliyeti sınırlar)

Hedef (gerçek yapay zekâ ile): en az %95'i kontrolden geçsin, yanlış alet öneren hiç olmasın.
"""
import argparse
import json
import statistics
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))

from app.service import FeedbackService  # noqa: E402
import fakes  # noqa: E402

FAKES = {"good": fakes.GoodWriter, "wrong": fakes.WrongInstrumentWriter,
         "source": fakes.MadeUpSourceWriter, "broken": fakes.BrokenWriter}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fake", choices=FAKES)
    ap.add_argument("--limit", type=int)
    a = ap.parse_args()
    svc = FeedbackService(writer=FAKES[a.fake]()) if a.fake else FeedbackService()
    k = svc.knowledge

    # 1) Her aktif hata senaryosu bir test vakası
    cases = [{"stepId": step, "scenarioId": sid, "deviationType": "WRONG_INSTRUMENT",
              "expected": None, "actual": None} for sid, (step, _) in k.scenarios.items()]
    if a.limit:
        cases = cases[: a.limit]
    sources, problems, durations, empty = Counter(), Counter(), [], 0
    wrong_instrument = 0
    for c in cases:
        ans = svc.explain(c)
        sources[ans["source"]] += 1
        durations.append(ans["durationMs"])
        for p in ans["checks"]:
            problems[p.split(":")[0]] += 1
            if "beklenmeyen alet" in p or "yanlış verilen alet" in p:
                wrong_instrument += 1
        if not ans["explanation"].strip() or not ans["recommendedAction"].strip():
            empty += 1

    # 2) Senaryo kimliği verilmeden, yanlış aletten senaryo bulma
    match = json.loads((HERE / "matching_cases.json").read_text(encoding="utf-8"))
    hits = []
    for m in match:
        got, _ = k.find(m["stepId"], None, m["actual"], k.expected_instruments(m["stepId"], m["parameters"]),
                        k.wanted_instruments(m["stepId"], m["expected"], m["parameters"]), m["parameters"])
        hits.append((m, got))

    n = len(cases)
    print(f"yazıcı: {svc.writer_mode} · model: {k.version} · vaka: {n}")
    print(f"  yapay zekâ cevabı (llm): {sources['llm']} (%{100 * sources['llm'] / n:.1f})")
    print(f"  hazır metin (table):     {sources['table']} (%{100 * sources['table'] / n:.1f})")
    print(f"  boş cevap:               {empty}")
    print(f"  kontrolün yakaladığı yanlış alet önerisi: {wrong_instrument}")
    if problems:
        print("  kontrol sorunları:")
        for p, cnt in problems.most_common(8):
            print(f"    {cnt:4d}  {p}")
    print(f"  süre: ortanca {statistics.median(durations):.0f} ms · en uzun {max(durations):.0f} ms")
    ok = sum(1 for m, got in hits if got == m["scenario"])
    print(f"senaryo eşleştirme: {ok}/{len(hits)}")
    for m, got in hits:
        if got != m["scenario"]:
            print(f"  ✗ adım {m['stepId']} {m['actual']}: beklenen {m['scenario']}, bulunan {got}")
    if svc.writer_mode != "none" and not a.fake:
        rate = sources["llm"] / n
        print("HEDEF:", "TUTTU" if rate >= 0.95 else "TUTMADI", f"(kontrolden geçen %{100 * rate:.1f}, hedef %95)")


if __name__ == "__main__":
    main()
