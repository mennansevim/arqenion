"""Eski 25 adımlı backend modelinden (v1) gelen sapmaların v2.4'e çevrilmesi.

Backend'in gönderebileceği her durumu dener: her adım × her yanlış alet/sütür, her komplikasyonun tetiklenmesi
ve çözüm zincirinin her adımında yanlış malzeme. Çalıştırma: python -m pytest -q
"""
import json
from pathlib import Path

import pytest

import fakes
from app.legacy import LegacyV1
from app.service import FeedbackService

V1 = json.loads((Path(__file__).resolve().parents[2] / "backend" / "Procedures" / "open-appendectomy-v1.json")
                .read_text(encoding="utf-8"))
V1_STEPS = {s["stepId"]: s for s in V1["steps"]}
V1_CODES = [i["code"] for i in V1["instrumentCatalog"]]
V1_SUTURES = [s["code"] for s in V1["sutureCatalog"]]
LEGACY = LegacyV1()


@pytest.fixture(scope="module")
def knowledge():
    return FeedbackService(writer=None).knowledge


@pytest.fixture(scope="module")
def good(knowledge):
    return FeedbackService(writer=fakes.GoodWriter(), knowledge=knowledge)


@pytest.fixture(scope="module")
def table(knowledge):
    return FeedbackService(writer=None, knowledge=knowledge)


def active(c, index):
    """Backend'deki ProcedureService.BuildActive ile aynı biçim (camelCase JSON)."""
    actions = [{"order": i + 1, "title": r["title"], "expectedEvent": r["expectedEvent"], "target": r.get("target"),
                "allowedInstruments": r["allowedInstruments"], "requiredSuture": r.get("requiredSuture"),
                "note": r.get("note"), "done": i < index} for i, r in enumerate(c["resolution"])]
    return {"complicationId": c["complicationId"], "title": c["title"], "narrative": c["narrative"],
            "resolutionNote": c["resolutionNote"], "severity": c["severity"], "stepId": c["stepId"],
            "scrubResolvable": c["scrubResolvable"], "resolutionIndex": index, "resolutionTotal": len(actions),
            "resolved": index >= len(actions), "currentAction": actions[index] if index < len(actions) else None,
            "actions": actions}


def payload(step, dev_type, expected, actual, complication=None):
    """Backend AiFeedbackService'in gönderdiği gövde."""
    return {"sessionId": "t", "eventId": "e", "stepId": step, "deviationType": dev_type, "expected": expected,
            "actual": actual, "severity": "MEDIUM", "parameters": {"PATIENT_BODY_TYPE": "NORMAL"},
            "procedureModel": "v1", "complication": complication}


# --- Tablo eksiksiz mi -------------------------------------------------------
def test_every_v1_step_maps_to_a_v24_step(knowledge):
    assert set(LEGACY.steps) == set(V1_STEPS)
    for entry in LEGACY.steps.values():
        assert entry["step"] in knowledge.steps
        assert entry.get("sutureStep", entry["step"]) in knowledge.steps


def test_every_v1_code_is_known(knowledge):
    for code in V1_CODES + V1_SUTURES:
        assert code in LEGACY.instruments or code in LEGACY.legacy_only, code
    for target in LEGACY.instruments.values():
        assert target in knowledge.instruments and not knowledge.instruments[target].get("legacy"), target
    for code in LEGACY.legacy_only:
        assert code not in LEGACY.instruments


def test_every_complication_maps_to_its_source_step():
    for c in V1["complications"]:
        assert LEGACY.complications[c["complicationId"]]["step"] == c["sourceSteps"][0]


# --- Her adım × her yanlış malzeme --------------------------------------------
def wrong_choices():
    for sid, s in V1_STEPS.items():
        if s["expectedEvent"] == "count_confirmed":
            yield sid, "COUNT_MISMATCH", "RADIOPAQUE_LAP:10", "RADIOPAQUE_LAP:9"
            continue
        expected = " / ".join(s["allowedInstruments"])
        for code in V1_CODES:
            if code not in s["allowedInstruments"]:
                yield sid, "WRONG_INSTRUMENT", expected, code
        if s.get("requiredSuture"):
            for code in V1_SUTURES:
                if code != s["requiredSuture"]:
                    yield sid, "WRONG_INSTRUMENT", s["requiredSuture"], code
        yield sid, "WRONG_ORDER", s["expectedEvent"], "count_confirmed"
        yield sid, "STERILE_VIOLATION", None, None


CASES = list(wrong_choices())


def test_all_cases_are_listed():
    assert len(CASES) > 1000


@pytest.mark.parametrize("service_name", ["good", "table"])
def test_every_v1_deviation_gets_an_answer(request, knowledge, service_name):
    svc = request.getfixturevalue(service_name)
    for sid, dev_type, expected, actual in CASES:
        ans = svc.explain(payload(sid, dev_type, expected, actual))
        where = f"v1 adım {sid} {dev_type} {expected} → {actual}"
        assert ans["stepId"] == sid, where
        assert ans["mappedStepId"] in knowledge.steps, where
        assert ans["procedureVersion"] == knowledge.version
        if ans["source"] == "llm":
            assert ans["checks"] == [] and ans["possibleRisk"] and ans["recommendedAction"], where
        conflict = any(c.startswith("çelişki") for c in ans["checks"])
        if not conflict:
            assert ans["explanation"], where


def test_ai_never_recommends_what_the_engine_rejects(good, knowledge):
    for sid, dev_type, expected, actual in CASES:
        dev = LEGACY.translate(payload(sid, dev_type, expected, actual), knowledge)
        if dev["allowed"] is None:
            continue
        engine = LEGACY.codes(expected, knowledge.instruments)
        mapped_actual = LEGACY.codes(actual, knowledge.instruments)
        assert not set(mapped_actual) & set(dev["allowed"]), f"v1 adım {sid}: {actual} önerilebilir kaldı"
        kinds = {knowledge.instruments[c]["category"] for c in engine}
        for code in dev["allowed"]:
            if knowledge.instruments[code]["category"] in kinds:
                assert code in engine, f"v1 adım {sid}: motorun kabul etmediği {code} önerilebilir"


# --- Bilinen çelişkiler --------------------------------------------------------
@pytest.mark.parametrize("step,expected,actual", [
    (22, "PROLENE_0", "VICRYL_2_0"),          # v1 fasya: 0 Prolen; v2.4: 0 Vicryl
    (21, "VICRYL_2_0", "VICRYL_1_0"),         # v1 kas: 2/0 Vicryl; v2.4: 0/1 Vicryl (verilen v2.4'te doğru)
    (3, "METZENBAUM_SCISSORS / MOSQUITO_CLAMP / CRILE_CLAMP_CURVED", "CRILE_CLAMP_STRAIGHT"),
])
def test_conflict_goes_to_backend_table(good, step, expected, actual):
    ans = good.explain(payload(step, "WRONG_INSTRUMENT", expected, actual))
    assert ans["source"] == "table" and ans["possibleRisk"] == ""   # boş risk → backend kendi tablosunu kullanır
    assert ans["checks"][0].startswith("çelişki")


def test_no_conflict_on_agreeing_step(good):
    ans = good.explain(payload(24, "WRONG_INSTRUMENT", "POLYAMIDE_4_0", "VICRYL_2_0"))
    assert ans["source"] == "llm" and ans["mappedStepId"] == 56


def test_suture_error_on_purse_string_uses_preparation_step(good):
    ans = good.explain(payload(10, "WRONG_INSTRUMENT", "SILK_3_0", "VICRYL_2_0"))
    assert ans["mappedStepId"] == 26 and ans["scenarioId"] == "S26-E1"


# --- Komplikasyonlar -------------------------------------------------------------
TRIGGERS = [(c, code) for c in V1["complications"]
            for code in (c.get("triggerInstruments") or []) + (c.get("triggerSutures") or [])]
MISSTEPS = [(c, i) for c in V1["complications"] for i in range(len(c["resolution"]))]


def trigger_payload(c, code):
    s = V1_STEPS[c["stepId"]]
    expected = s.get("requiredSuture") if code in V1_SUTURES else " / ".join(s["allowedInstruments"])
    dev_type = "WRONG_INSTRUMENT" if c["scrubResolvable"] and c["resolution"] else "CONTAMINATED_TOOL_REUSE"
    resolved = not (c["scrubResolvable"] and c["resolution"])
    return payload(c["stepId"], dev_type, expected, code, active(c, len(c["resolution"]) if resolved else 0))


@pytest.mark.parametrize("c,code", TRIGGERS, ids=[f"{c['complicationId']}-{x}" for c, x in TRIGGERS])
def test_complication_trigger_is_explained_with_its_story(good, table, c, code):
    ans = good.explain(trigger_payload(c, code))
    assert ans["source"] == "llm", ans["checks"]
    assert ans["mappedStepId"] == LEGACY.complications[c["complicationId"]]["step"]
    assert f"legacy-v1:complication:{c['complicationId']}" in ans["sources"]
    fallback = table.explain(trigger_payload(c, code))
    assert fallback["source"] == "table" and fallback["possibleRisk"] == c["narrative"]


@pytest.mark.parametrize("c,index", MISSTEPS, ids=[f"{c['complicationId']}-{i + 1}" for c, i in MISSTEPS])
def test_wrong_material_during_resolution(good, table, knowledge, c, index):
    r = c["resolution"][index]
    expected = " / ".join(r["allowedInstruments"]) + (f" + {r['requiredSuture']}" if r.get("requiredSuture") else "")
    wrong = "SCALPEL_NO_20" if "SCALPEL_NO_20" not in r["allowedInstruments"] else "MAYO_SCISSORS"
    p = payload(c["stepId"], "COMPLICATION_MISMANAGED", expected, wrong, active(c, index))

    ans = good.explain(p)
    assert ans["source"] == "llm", ans["checks"]
    assert ans["scenarioId"] is None     # adımın hata senaryoları komplikasyon yönetimini anlatmaz
    assert ans["sources"][0] == f"legacy-v1:complication:{c['complicationId']}"
    wanted = LEGACY.codes(expected, knowledge.instruments)
    assert knowledge.instrument_name(wanted[0]) in ans["recommendedAction"]

    fallback = table.explain(p)
    assert fallback["possibleRisk"] == c["narrative"]
    assert r["title"] in fallback["explanation"]


def test_complication_chunk_marks_the_current_action(knowledge):
    c = next(x for x in V1["complications"] if x["complicationId"] == "CECUM_TEAR_SPILLAGE")
    text = LEGACY.complication_chunk(active(c, 2), knowledge)["text"]
    assert text.count("← şu an beklenen") == 1
    assert "3. " + c["resolution"][2]["title"] in text and "Aspiratör" in text.split("← şu an beklenen")[0].splitlines()[-1]


def test_native_v24_request_is_unchanged(good):
    ans = good.explain({"stepId": 52, "deviationType": "WRONG_INSTRUMENT",
                        "expected": "SUTURE_POLYGLACTIN_0", "actual": "SUTURE_POLYPROPYLENE_0"})
    assert ans["stepId"] == 52 and "mappedStepId" not in ans and ans["source"] == "llm"
