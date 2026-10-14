# -*- coding: utf-8 -*-
"""Koşula göre akış hesaplama ve çizim (deterministik — LLM yolu kendisi uydurmaz). v2

Python:
    from flow_engine import FlowEngine
    fe = FlowEngine("../dist/open-appendectomy-v2.4.graph.json")
    r = fe.walk({"ADHESIONS_PRESENT": True, "BLEEDING_AFTER_IRRIGATION": [True, False]},
                parameters={"PATIENT_BODY_TYPE": "OBESE",
                            "MESO_VESSELS": [{"caliber": "SMALL"}, {"caliber": "LARGE"}]})
    print(fe.mermaid(r, from_step=40, to_step=48))

Koşul değerleri:
    True / False          → her değerlendirmede aynı
    [True, False, ...]    → aynı koşulun ardışık değerlendirmeleri (döngülerde: 1. yıkamada kanama var, 2.'de yok)
    verilmeyen koşul      → False; ama source=ADAPTIVE ise sonuçta `adaptiveDecisions` listesine "karar bekliyor" olarak düşer

CLI:
    python3 flow_engine.py --cond ADHESIONS_PRESENT=true --cond BLEEDING_AFTER_IRRIGATION=true,false --param PATIENT_BODY_TYPE=OBESE --from 38 --to 50
    python3 flow_engine.py --full
"""
import json, argparse, re, os
DEFAULT_GRAPH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "dist", "open-appendectomy-v2.4.graph.json")


def tool_schema(graph):
    """LLM function-calling için araç tanımı (koşullar graph.json'dan üretilir)."""
    props = {c: {"description": f"{m['description']} (kaynak: {m.get('source')})",
                 "anyOf": [{"type": "boolean"}, {"type": "array", "items": {"type": "boolean"}}]}
             for c, m in graph["conditions"].items() if m.get("kind") != "OUTCOME"}
    return {
        "name": "draw_surgical_flow",
        "description": "Açık apendektomi akışını verilen koşul ve hasta parametrelerine göre hesaplar, Mermaid diyagramı ve adım başına beklenen aletleri döndürür. Verilmeyen koşul false kabul edilir.",
        "input_schema": {
            "type": "object",
            "properties": {
                "conditions": {"type": "object", "properties": props, "additionalProperties": False},
                "parameters": {"type": "object", "properties": {
                    k: ({"type": "string", "enum": p["values"]} if p["type"] == "enum" else
                        {"type": "array", "items": {"type": "object", "properties": {"caliber": {"type": "string", "enum": p["items"]["caliber"]}}}})
                    for k, p in graph.get("parameters", {}).items()}, "additionalProperties": False},
                "from_step": {"type": "integer", "minimum": 1, "maximum": 60},
                "to_step": {"type": "integer", "minimum": 1, "maximum": 60},
                "include_preop": {"type": "boolean", "default": False},
                "show_skipped": {"type": "boolean", "default": True},
            },
        },
    }


class FlowEngine:
    def __init__(self, graph_path):
        g = json.load(open(graph_path))
        self.g = g
        self.nodes = {n["id"]: n for n in g["nodes"]}
        self.phase = {p["id"]: p["name"] for p in g["phases"]}
        self.subflow = {s["id"]: s["name"] for s in g.get("subflows", [])}
        self.TOOL_SCHEMA = tool_schema(g)

    # ---------- yürüyüş ----------
    def walk(self, conditions=None, parameters=None, include_preop=False, max_loop=1, loop_limits=None):
        conds = {}
        for k, v in (conditions or {}).items():
            if k not in self.g["conditions"]:
                raise ValueError(f"Bilinmeyen koşul: {k}")
            conds[k] = list(v) if isinstance(v, (list, tuple)) else [bool(v)]
        params = dict(self.g.get("parameterDefaults", {}))
        params.update(parameters or {})
        limits = dict(loop_limits or {})   # çağıranın üst sınırı > modeldeki maxIterations > max_loop

        evals, taken, adaptive = {}, {}, []
        path, edges = [], []
        cur = self.g["entry"] if include_preop else 1
        guard = 0
        while cur is not None:
            guard += 1
            if guard > 500:
                raise RuntimeError("Akış döngüye girdi")
            path.append(cur)
            n = self.nodes[cur]
            nxt, label = n["next"], None
            for b in n.get("branches", []):
                c = b["when"]
                lim = limits.get(c, b.get("maxIterations", max_loop))
                if taken.get((cur, c), 0) >= lim:
                    continue
                i = evals.get((cur, c), 0)
                evals[(cur, c)] = i + 1
                seq = conds.get(c)
                if seq is None:
                    val = False
                    if self.g["conditions"][c].get("source") == "ADAPTIVE":
                        adaptive.append({"atNode": cur, "condition": c, "evaluation": i + 1, "assumed": False})
                else:
                    val = bool(seq[i] if i < len(seq) else seq[-1] if len(seq) == 1 else False)
                if val:
                    taken[(cur, c)] = taken.get((cur, c), 0) + 1
                    nxt, label = b["goTo"], c
                    break
            if nxt is not None:
                edges.append((cur, nxt, label))
            cur = nxt

        visited = set(path)
        skipped = [x["id"] for x in self.g["nodes"] if x["kind"] != "preop" and x["id"] not in visited]
        expected = {str(p): self.expected_instruments(p, params) for p in dict.fromkeys(path) if self.nodes[p]["kind"] != "preop"}
        return {"conditions": {k: v for k, v in conds.items()}, "parameters": params, "path": path, "edges": edges,
                "skippedSteps": skipped, "stepCount": sum(1 for p in path if self.nodes[p]["kind"] != "preop"),
                "expectedInstruments": expected, "adaptiveDecisions": adaptive}

    def expected_instruments(self, node_id, params):
        """Adımda kabul edilen aletler; vücut tipi ve damar parametreleri uygulanmış."""
        n = self.nodes[node_id]
        out = {"accepted": list(n.get("accepted", []))}
        for r in n.get("parameterRules", []):
            v = params.get(r["parameter"])
            if v in r["expectedInstruments"]:
                out["byParameter"] = {"parameter": r["parameter"], "value": v, "instruments": r["expectedInstruments"][v]}
        dq = n.get("dynamicQuantity")
        if dq:
            vessels = params.get(dq["parameter"]) or []
            counts = {}
            for vsl in vessels:
                code = dq["instrumentByCaliber"][vsl["caliber"]]
                counts[code] = counts.get(code, 0) + 1
            out["clamps"] = [{"code": c, "qty": q} for c, q in counts.items()]
            hemostats = set(dq["instrumentByCaliber"].values())
            out["accepted"] = [x for x in out["accepted"] if x not in hemostats] + list(counts)
        if n.get("pending"):
            out["pendingConfirmation"] = n["pending"]
        return out

    # ---------- Mermaid ----------
    @staticmethod
    def _nid(x):
        return f"S{x}" if isinstance(x, int) or (isinstance(x, str) and x[:1].isdigit()) else str(x)

    @staticmethod
    def _esc(t):
        return re.sub(r'["\[\]{}()<>]', "", t)

    def _shape(self, n):
        lbl = self._esc(f"{n['id']}. {n['label']}" if n["kind"] != "preop" else f"{n['id']} {n['label']}")
        if n.get("instrument") and n["instrument"] != "HAND":
            lbl += f"<br/><small>{n['instrument']}</small>"
        i = self._nid(n["id"])
        if n.get("isDecision"):
            return f'{i}{{"{lbl}"}}'
        if n["kind"] == "preop":
            return f'{i}(["{lbl}"])'
        if n["kind"] == "subflow":
            return f'{i}[/"{lbl}"/]'
        return f'{i}["{lbl}"]'

    @staticmethod
    def _main_no(x):
        """Aralık filtresi için: 47a → 47, PRE01 → None."""
        if isinstance(x, int):
            return x
        m = re.match(r"(\d+)", str(x))
        return int(m.group(1)) if m else None

    def mermaid(self, result, from_step=None, to_step=None, show_skipped=True, by_phase=True):
        lo, hi = from_step or -1, to_step or 10 ** 9

        def inr(x):
            k = self._main_no(x)
            return k is None or lo <= k <= hi

        shown = [p for p in dict.fromkeys(result["path"]) if inr(p)]
        skipped = [s for s in result["skippedSteps"] if inr(s)] if show_skipped else []
        L = ["flowchart TD",
             "classDef cond fill:#fff4d6,stroke:#c98a00;",
             "classDef dec fill:#e6f0ff,stroke:#2f6fd6;",
             "classDef sub fill:#fde8e1,stroke:#c0532b;",
             "classDef skip fill:#f2f2f2,stroke:#bbb,color:#999,stroke-dasharray:4 3;",
             "classDef pre fill:#eef7ee,stroke:#3a8a3a;"]
        allids = shown + skipped
        if by_phase:
            groups = {}
            for x in allids:
                n = self.nodes[x]
                gid = n.get("subflowId") or n.get("phaseId") or "PREOP"
                groups.setdefault(gid, []).append(x)
            for gid, xs in groups.items():
                title = self.phase.get(gid) or self.subflow.get(gid) or "Ameliyat öncesi"
                L.append(f'subgraph {gid}["{self._esc(title)}"]')
                L += ["  " + self._shape(self.nodes[x]) for x in xs]
                L.append("end")
        else:
            L += [self._shape(self.nodes[x]) for x in allids]
        for a, b, lab in dict.fromkeys(map(tuple, result["edges"])):
            if inr(a) and inr(b):
                L.append(f"{self._nid(a)} -->|{lab}| {self._nid(b)}" if lab else f"{self._nid(a)} --> {self._nid(b)}")
        for s in skipped:
            for n in self.g["nodes"]:
                for br in n.get("branches", []):
                    if br["goTo"] == s and inr(n["id"]) and n["id"] in allids:
                        L.append(f"{self._nid(n['id'])} -.->|{br['when']}=false| {self._nid(s)}")
        for x in allids:
            n = self.nodes[x]
            cls = "skip" if x in skipped else ("dec" if n.get("isDecision") else
                                               {"conditional": "cond", "subflow": "sub", "preop": "pre"}.get(n["kind"]))
            if cls:
                L.append(f"class {self._nid(x)} {cls};")
        return "\n".join(L)

    def full_mermaid(self):
        """Tüm dallar ve alt akışlar dahil genel akış."""
        ids = [n["id"] for n in self.g["nodes"] if n["kind"] != "preop"]
        res = {"path": ids, "skippedSteps": [], "edges": []}
        for n in self.g["nodes"]:
            if n["kind"] == "preop":
                continue
            if n["next"] is not None:
                res["edges"].append((n["id"], n["next"], "else" if n["branches"] else None))
            for b in n["branches"]:
                res["edges"].append((n["id"], b["goTo"], b["when"]))
        return self.mermaid(res)

    def run_tool(self, args):
        """LLM araç çağrısını karşılar."""
        r = self.walk(args.get("conditions"), args.get("parameters"), include_preop=args.get("include_preop", False))
        return {"path": r["path"], "skippedSteps": r["skippedSteps"], "stepCount": r["stepCount"],
                "expectedInstruments": r["expectedInstruments"], "adaptiveDecisions": r["adaptiveDecisions"],
                "mermaid": self.mermaid(r, args.get("from_step"), args.get("to_step"), args.get("show_skipped", True))}


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--graph", default=DEFAULT_GRAPH)
    ap.add_argument("--cond", action="append", default=[], help="KOD=true | KOD=true,false")
    ap.add_argument("--param", action="append", default=[], help="PATIENT_BODY_TYPE=OBESE | MESO_VESSELS=SMALL,LARGE")
    ap.add_argument("--from", dest="frm", type=int)
    ap.add_argument("--to", type=int)
    ap.add_argument("--preop", action="store_true")
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--json", action="store_true")
    a = ap.parse_args()
    fe = FlowEngine(a.graph)
    if a.full:
        print(fe.full_mermaid())
        raise SystemExit
    truthy = ("1", "true", "evet", "yes")
    conds = {}
    for c in a.cond:
        k, v = c.split("=")
        vals = [x.strip().lower() in truthy for x in v.split(",")]
        conds[k] = vals if len(vals) > 1 else vals[0]
    params = {}
    for p in a.param:
        k, v = p.split("=")
        params[k] = [{"caliber": x} for x in v.split(",")] if k == "MESO_VESSELS" else v
    r = fe.walk(conds, params, include_preop=a.preop)
    print(json.dumps(r, ensure_ascii=False, indent=1) if a.json else fe.mermaid(r, a.frm, a.to))
