"""Servis katmanı: akışı çalıştırır, süreyi sınırlar, her isteği kayda geçirir."""
import json
import time
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FutureTimeout
from datetime import datetime, timezone

from .config import settings as default_settings
from .fallback import table_answer
from .graph import build_graph
from .knowledge import Knowledge
from .legacy import LegacyV1
from .writer import make_writer


def install_descriptors(knowledge, path) -> None:
    """Alet adında geçmeyen niteleyicileri (ör. Ochsner-Kocher → "dişli") senaryo eşleştirmeye ekler.
    Yanıltıcı aletlerin kendisi (No:11 bistüri, Mayo makas…) model kataloğunda."""
    try:
        knowledge.descriptors.update(json.loads(path.read_text(encoding="utf-8")).get("descriptors", {}))
    except (OSError, ValueError):
        pass


class FeedbackService:
    def __init__(self, settings=default_settings, writer="auto", knowledge=None):
        self.settings = settings
        self.knowledge = knowledge or Knowledge(settings.model_dir, settings.model_version, settings.engine_dir)
        self.legacy = LegacyV1()
        self.legacy.install(self.knowledge)   # yazıcıya verilen alet adları bu katalogdan alınır; grafikten önce
        install_descriptors(self.knowledge, settings.descriptors_path)
        self.writer = make_writer(settings) if writer == "auto" else writer
        self.graph = build_graph(self.knowledge, self.writer)
        self._pool = ThreadPoolExecutor(max_workers=8)

    @property
    def writer_mode(self) -> str:
        return getattr(self.writer, "mode", "none") if self.writer else "none"

    def explain(self, deviation: dict) -> dict:
        t0 = time.perf_counter()
        if deviation.get("procedureModel") == "v1":
            deviation = self.legacy.translate(deviation, self.knowledge)
        future = self._pool.submit(self.graph.invoke, {"deviation": deviation})
        try:
            state = future.result(timeout=self.settings.ai_timeout_sec + 1)
            answer = state["answer"]
            timings = state.get("timings", {})
        except FutureTimeout:
            answer, timings = self._emergency(deviation, "zaman aşımı"), {}
        except Exception as exc:  # akışta beklenmeyen bir hata olsa bile VR cevapsız kalmaz
            answer, timings = self._emergency(deviation, f"{type(exc).__name__}: {exc}"[:300]), {}
        if "legacy" in deviation:   # backend kendi (eski) adım numarasını geri alır
            answer["mappedStepId"] = answer["stepId"]
            answer["stepId"] = deviation["legacy"]["stepId"]
        answer["durationMs"] = round((time.perf_counter() - t0) * 1000, 1)
        self._log(deviation, answer, timings)
        return answer

    def _emergency(self, deviation, reason) -> dict:
        k = self.knowledge
        step = deviation["stepId"]
        expected = deviation["allowed"] if deviation.get("allowed") is not None else k.expected_instruments(step, deviation.get("parameters"))
        wanted = k.wanted_instruments(step, deviation.get("expected"), deviation.get("parameters"))
        scenario_id, chunks = k.find(step, deviation.get("scenarioId"), deviation.get("actual"), expected, wanted, deviation.get("parameters"))
        if deviation.get("contextOnly") or deviation.get("noScenario"):
            scenario_id = None
        chunks = (deviation.get("context") or []) + ([] if deviation.get("contextOnly") else chunks)
        return {
            "sessionId": deviation.get("sessionId", ""), "eventId": deviation.get("eventId", ""),
            "stepId": step, "deviationType": deviation.get("deviationType", ""),
            **table_answer(k, step, scenario_id, wanted, deviation.get("actual"),
                           complication=deviation.get("complication"), conflict=bool(deviation.get("conflict"))),
            "source": "table",
            "severity": deviation.get("severity", "MEDIUM"),
            "modelName": None, "promptTokens": None, "completionTokens": None,
            "scenarioId": scenario_id, "sources": [c["id"] for c in chunks],
            "checks": [reason], "procedureVersion": k.version,
        }

    def _log(self, deviation, answer, timings) -> None:
        try:
            self.settings.log_dir.mkdir(parents=True, exist_ok=True)
            now = datetime.now(timezone.utc)
            path = self.settings.log_dir / f"feedback-{now:%Y-%m-%d}.jsonl"
            row = {"at": now.isoformat(), "writer": self.writer_mode, "input": deviation,
                   "answer": answer, "timings": timings}
            with path.open("a", encoding="utf-8") as f:
                f.write(json.dumps(row, ensure_ascii=False) + "\n")
        except OSError:
            pass  # kayıt yazılamasa da cevap gider
