"""Çalıştırma: python -m pytest -q"""
import json
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

import fakes
from app import api
from app.service import FeedbackService

HERE = Path(__file__).resolve().parent
PROLEN_52 = {"stepId": 52, "deviationType": "WRONG_INSTRUMENT",
             "expected": "SUTURE_POLYGLACTIN_0", "actual": "SUTURE_POLYPROPYLENE_0"}


@pytest.fixture(scope="module")
def knowledge():
    return FeedbackService(writer=None).knowledge


def svc(knowledge, writer):
    return FeedbackService(writer=writer, knowledge=knowledge)


# --- Bul -------------------------------------------------------------------
def test_scenario_id_is_used_directly(knowledge):
    sid, chunks = knowledge.find(52, "S52-E6")
    assert sid == "S52-E6"
    assert chunks[0]["id"].endswith(":error:S52-E6")


@pytest.mark.parametrize("case", json.loads((HERE / "matching_cases.json").read_text(encoding="utf-8")))
def test_scenario_found_from_wrong_instrument(knowledge, case):
    k = knowledge
    sid, _ = k.find(case["stepId"], None, case["actual"], k.expected_instruments(case["stepId"], case["parameters"]),
                    k.wanted_instruments(case["stepId"], case["expected"], case["parameters"]), case["parameters"])
    assert sid == case["scenario"]


def test_expected_follows_patient_parameter(knowledge):
    aesthetic = knowledge.expected_instruments(56, {"SKIN_CLOSURE_TYPE": "AESTHETIC"})
    standard = knowledge.expected_instruments(56, {"SKIN_CLOSURE_TYPE": "STANDARD"})
    assert "SUTURE_POLIGLECAPRONE_4_0" in aesthetic and "SUTURE_POLYPROPYLENE_3_0" not in aesthetic
    assert "SUTURE_POLYPROPYLENE_3_0" in standard and "SUTURE_POLIGLECAPRONE_4_0" not in standard


# --- Yaz → Kontrol → Gönder ------------------------------------------------
def test_good_draft_goes_out_as_llm(knowledge):
    ans = svc(knowledge, fakes.GoodWriter()).explain(PROLEN_52)
    assert ans["source"] == "llm" and ans["checks"] == []


@pytest.mark.parametrize("writer,problem", [
    (fakes.WrongInstrumentWriter(), "yanlış verilen alet"),
    (fakes.MadeUpSourceWriter(), "bulunmayan kaynak"),
    (fakes.BrokenWriter(), "TimeoutError"),
    (None, "OPENAI_API_KEY"),
])
def test_bad_or_missing_ai_falls_back_to_clinical_text(knowledge, writer, problem):
    ans = svc(knowledge, writer).explain(PROLEN_52)
    assert ans["source"] == "table"
    assert any(problem in p for p in ans["checks"])
    assert "Prolen" in ans["explanation"]                     # onaylı S52-E6 metni
    assert "Vicryl" in ans["recommendedAction"]               # "bildir" adımı değil, doğru ipliği hazırlama adımı


def test_every_active_scenario_has_a_clinical_answer(knowledge):
    s = svc(knowledge, None)
    for sid, (step, _) in knowledge.scenarios.items():
        ans = s.explain({"stepId": step, "scenarioId": sid})
        assert ans["explanation"].strip() and ans["recommendedAction"].strip(), sid


def test_unknown_step_does_not_crash(knowledge):
    ans = svc(knowledge, None).explain({"stepId": 999, "actual": "X"})
    assert ans["source"] == "table"


# --- Web servisi -----------------------------------------------------------
def test_api_feedback_and_health(knowledge, monkeypatch):
    monkeypatch.setattr(api, "_service", svc(knowledge, fakes.GoodWriter()))
    client = TestClient(api.app)
    assert client.get("/health").json()["status"] == "ok"
    r = client.post("/feedback", json=PROLEN_52)
    assert r.status_code == 200
    body = r.json()
    assert body["source"] == "llm" and body["stepId"] == 52 and body["scenarioId"] == "S52-E6"


# --- Yanıltıcı aletler (model kataloğu, clinical_review.py 6. tur) ----------
@pytest.mark.parametrize("step,expected,actual,scenario", [
    (1, "SCALPEL_BLADE_10", "SCALPEL_BLADE_11", "S01-E1"),
    (20, "CLAMP_GEMINI_CURVED_20", "CLAMP_OVERHOLT_GEISSENDOERFER_21", "S20-E1"),
    (10, "SCISSORS_METZENBAUM", "SCISSORS_MAYO", "S10-E1"),
])
def test_distractor_finds_its_approved_scenario(knowledge, step, expected, actual, scenario):
    ans = svc(knowledge, fakes.GoodWriter()).explain(
        {"stepId": step, "deviationType": "WRONG_INSTRUMENT", "expected": expected, "actual": actual, "procedureModel": "v2.4"})
    assert ans["scenarioId"] == scenario and ans["source"] == "llm"


def test_title_number_must_match_given_instrument(knowledge):
    # No:15 verildiğinde "10 yerine 11 numaralı bistüri" senaryosu seçilmemeli.
    assert knowledge.match_scenario(1, "SCALPEL_BLADE_15", ["SCALPEL_BLADE_10"]) != "S01-E1"
    assert knowledge.match_scenario(51, "CLAMP_ALLIS_15", ["CLAMP_ALLIS_ATRAUMATIC_19"]) == "S51-E2"


@pytest.mark.parametrize("step,actual,wanted,scenario", [
    # backend.Tests/ScenarioDecisionTests ile aynı zor vakalar: iki taraf aynı onaylı senaryoyu bulmalı.
    (30, "CLAMP_OCHSNER_KOCHER_CURVED_20", "CLAMP_CRILE_STRAIGHT_14", "S30-E2"),
    (30, "CLAMP_CRILE_CURVED_14", "CLAMP_CRILE_STRAIGHT_14", "S30-E1"),
    (51, "CLAMP_OCHSNER_KOCHER_CURVED_20", "CLAMP_ALLIS_ATRAUMATIC_19", "S51-E1"),
    (51, "CLAMP_ALLIS_15", "CLAMP_ALLIS_ATRAUMATIC_19", "S51-E2"),
    (15, "CLAMP_ALLIS_15", "CLAMP_BABCOCK_16", "S15-E1"),
    (26, "SUTURE_POLYGLACTIN_3_0", "SUTURE_SILK_3_0", "S26-E1"),
    (42, "CHLORHEXIDINE_SOLUTION", "ISOTONIC_SALINE", "S42-E1"),
    (56, "SUTURE_POLYPROPYLENE_2_0", "SUTURE_POLYPROPYLENE_4_0", "S56-E2"),
])
def test_same_scenario_as_backend(knowledge, step, actual, wanted, scenario):
    assert knowledge.match_scenario(step, actual, [wanted]) == scenario
