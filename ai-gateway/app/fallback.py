"""Hazır klinik metin: yapay zekâ yoksa, geç kaldıysa ya da kontrolden geçemediyse gönderilen metin.

Metin doğrudan modeldeki (klinik ekibin onayladığı) senaryodan gelir; hiçbir şey üretilmez.
"""
from .knowledge import _tokens


def _pick_action(knowledge, steps, wanted, actual) -> str:
    """Çözüm adımlarından doğru aleti hazırlamayı anlatanı seç (yanlış aleti anan 'bildir' adımı değil)."""
    good, bad = set(), knowledge.words_of(actual) if actual else set()
    for code in wanted:
        good |= knowledge.words_of(code)
    best, best_score = steps[0], 0
    for r in steps:
        words = _tokens(r)
        score = len(good & words) - len(bad & words)
        if score > best_score:
            best, best_score = r, score
    return best


def table_answer(knowledge, step, scenario_id, wanted, actual=None, complication=None, conflict=False) -> dict:
    scenario = knowledge.scenario(scenario_id) if scenario_id else None
    step_def = knowledge.steps.get(step) or {}
    names = ", ".join(knowledge.instrument_name(c) for c in wanted)
    if conflict:
        # v2.4 metni motorun kararıyla çelişir; risk boş gider, backend kendi klinik tablosunu kullanır.
        return {"possibleRisk": "", "explanation": f"Bu adımda beklenen: {names}." if names else "", "recommendedAction": ""}
    if complication:
        action = complication.get("currentAction") or {}
        return {
            "possibleRisk": complication.get("narrative") or complication.get("title", ""),
            "explanation": scenario["scrubError"] if scenario else (
                f"Komplikasyon yönetiminde sıradaki adım: {action['title']}." if action.get("title")
                else f"Komplikasyon: {complication.get('title', '')}."),
            "recommendedAction": (action.get("note") or action.get("title") or complication.get("resolutionNote", "")),
        }
    if scenario:
        steps = scenario["resolutionSteps"] or [""]
        return {
            "possibleRisk": scenario["possibleComplications"][0] if scenario["possibleComplications"] else "",
            "explanation": scenario["scrubError"],
            "recommendedAction": _pick_action(knowledge, steps, wanted, actual),
        }
    practice = step_def.get("scrub", {}).get("correctPractice", "")
    return {
        "possibleRisk": "",
        "explanation": f"Bu adımda beklenen: {names}." if names else "Bu adımda farklı bir uygulama bekleniyordu.",
        "recommendedAction": practice.split(". ")[0].rstrip(".") + "." if practice else "",
    }
