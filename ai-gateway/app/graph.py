"""AI akışı (LangGraph): Bul → Yaz → Kontrol → Gönder; kontrolden geçmezse Hazır metin → Gönder.
Eski modelle çelişen sapmada Bul → Hazır metin → Gönder (yapay zekâ çağrılmaz).

Bu akış YALNIZCA motor bir hata bulduğunda çalışır ve karar vermez.
"""
import time
from typing import Optional, TypedDict

from langgraph.graph import END, START, StateGraph

from .checks import check_draft
from .fallback import table_answer


class State(TypedDict, total=False):
    deviation: dict          # girdi: motorun bulduğu sapma
    expected: list           # motora göre bu adımda kabul edilen aletler
    wanted: list             # kursiyerin vermesi gereken alet(ler)
    scenario_id: Optional[str]
    chunks: list             # bulunan kaynak parçalar
    draft: Optional[dict]    # yapay zekânın taslağı
    writer_error: Optional[str]
    problems: list           # kontrolün bulduğu sorunlar
    answer: dict             # VR'a gidecek cevap
    timings: dict


def build_graph(knowledge, writer):
    names = {code: d.get("name", code) for code, d in knowledge.instruments.items()}

    def bul(state: State):
        t0 = time.perf_counter()
        d = state["deviation"]
        step = d["stepId"]
        # Eski modelden çevrilen sapmada kabul edilen aletleri motor belirler (legacy.py).
        expected = d["allowed"] if d.get("allowed") is not None else knowledge.expected_instruments(step, d.get("parameters"))
        wanted = knowledge.wanted_instruments(step, d.get("expected"), d.get("parameters"))
        if d.get("noScenario") or d.get("contextOnly"):   # komplikasyon yönetimi / çelişen adım: v2.4 senaryosu kullanılmaz
            scenario_id, chunks = None, knowledge.find(step, None, None, expected, wanted, d.get("parameters"))[1]
        else:
            scenario_id, chunks = knowledge.find(step, d.get("scenarioId"), d.get("actual"), expected, wanted, d.get("parameters"))
        chunks = (d.get("context") or []) + ([] if d.get("contextOnly") else chunks)
        out = {"expected": expected, "wanted": wanted, "scenario_id": scenario_id, "chunks": chunks,
               "timings": {"bulMs": round((time.perf_counter() - t0) * 1000, 1)}}
        if d.get("conflict"):
            out["problems"] = [d["conflict"]]
        return out

    def yaz(state: State):
        if writer is None:
            return {"draft": None, "writer_error": "yapay zekâ ayarlı değil (OPENAI_API_KEY yok)"}
        t0 = time.perf_counter()
        try:
            draft = writer.write(state["deviation"], state["expected"], state["chunks"], names)
            error = None
        except Exception as exc:  # zaman aşımı, ağ hatası, bozuk JSON… hepsi hazır metne düşer
            draft, error = None, f"{type(exc).__name__}: {exc}"[:300]
        timings = dict(state.get("timings", {}), yazMs=round((time.perf_counter() - t0) * 1000, 1))
        return {"draft": draft, "writer_error": error, "timings": timings}

    def kontrol(state: State):
        problems = check_draft(state.get("draft"), {c["id"] for c in state["chunks"]},
                               state["expected"], knowledge.instruments, state["deviation"].get("actual"))
        if state.get("writer_error"):
            problems = [state["writer_error"]] + [p for p in problems if p != "taslak yok"]
        return {"problems": problems}

    def hazir_metin(state: State):
        d = state["deviation"]
        body = table_answer(knowledge, d["stepId"], state.get("scenario_id"), state.get("wanted") or [], d.get("actual"),
                            complication=d.get("complication"), conflict=bool(d.get("conflict")))
        return {"answer": {**body, "source": "table"}}

    def gonder(state: State):
        d = state["deviation"]
        if state.get("answer"):
            body = state["answer"]
        else:
            draft = state["draft"]
            body = {k: draft[k] for k in ("possibleRisk", "explanation", "recommendedAction")}
            body["source"] = "llm"
        meta = (state.get("draft") or {}).get("_meta", {}) if body["source"] == "llm" else {}
        answer = {
            "sessionId": d.get("sessionId", ""),
            "eventId": d.get("eventId", ""),
            "stepId": d["stepId"],
            "deviationType": d.get("deviationType", ""),
            **body,
            "severity": d.get("severity", "MEDIUM"),
            "modelName": meta.get("model"),
            "promptTokens": meta.get("promptTokens"),
            "completionTokens": meta.get("completionTokens"),
            "scenarioId": state.get("scenario_id"),
            "sources": [c["id"] for c in state["chunks"]] if body["source"] == "table" else state["draft"]["sources"],
            "checks": state.get("problems", []),
            "procedureVersion": knowledge.version,
        }
        return {"answer": answer}

    def cakisma_var_mi(state: State) -> str:
        return "cakisma" if state["deviation"].get("conflict") else "yaz"

    def denetim_sonucu(state: State) -> str:
        return "gecmedi" if state["problems"] else "gecti"

    g = StateGraph(State)
    g.add_node("bul", bul)
    g.add_node("yaz", yaz)
    g.add_node("kontrol", kontrol)
    g.add_node("hazir_metin", hazir_metin)
    g.add_node("gonder", gonder)
    g.add_edge(START, "bul")
    g.add_conditional_edges("bul", cakisma_var_mi, {"yaz": "yaz", "cakisma": "hazir_metin"})
    g.add_edge("yaz", "kontrol")
    g.add_conditional_edges("kontrol", denetim_sonucu, {"gecti": "gonder", "gecmedi": "hazir_metin"})
    g.add_edge("hazir_metin", "gonder")
    g.add_edge("gonder", END)
    return g.compile()
