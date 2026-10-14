"""Komut satırından tek bir sapmayı dene.

Örnek:
  python cli.py --step 52 --expected SUTURE_POLYGLACTIN_0 --actual SUTURE_POLYPROPYLENE_0
  python cli.py --step 52 --scenario S52-E6
  python cli.py --graph            # akışın şemasını Mermaid olarak yazdırır
"""
import argparse
import json

from app.service import FeedbackService


def main():
    ap = argparse.ArgumentParser(description="Arqenion AI akışını tek bir sapmayla çalıştır")
    ap.add_argument("--step", type=int, default=52)
    ap.add_argument("--scenario")
    ap.add_argument("--expected")
    ap.add_argument("--actual")
    ap.add_argument("--type", default="WRONG_INSTRUMENT")
    ap.add_argument("--param", action="append", default=[], help="ör. SKIN_CLOSURE_TYPE=STANDARD")
    ap.add_argument("--graph", action="store_true")
    a = ap.parse_args()

    svc = FeedbackService()
    if a.graph:
        print(svc.graph.get_graph().draw_mermaid())
        return
    params = dict(p.split("=", 1) for p in a.param)
    answer = svc.explain({"stepId": a.step, "scenarioId": a.scenario, "expected": a.expected,
                          "actual": a.actual, "deviationType": a.type, "parameters": params})
    print(f"yazıcı: {svc.writer_mode}")
    print(json.dumps(answer, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
