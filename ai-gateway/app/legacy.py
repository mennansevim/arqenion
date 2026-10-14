"""Eski 25 adımlı backend modelinden (v1) gelen sapmayı 60 adımlık v2.4 modeline çevirir.

Backend bugün v1 ile çalışıyor; adım numaraları ve alet kodları v2.4'ten farklı. Tablo: legacy_v1_map.json.

Karar motorundadır: kursiyerin vermesi gereken alet her zaman backend motorunun beklediğidir.
v2.4 bu adımda motordan farklı düşünüyorsa (çelişki), yapay zekâ yazdırılmaz; backend kendi klinik tablosunu kullanır.
"""
import json
import re
from pathlib import Path

MAP_PATH = Path(__file__).resolve().parent / "legacy_v1_map.json"
CODE = re.compile(r"[A-Z][A-Z0-9_]{2,}")


class LegacyV1:
    def __init__(self, path: Path = MAP_PATH):
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        self.steps = {int(k): v for k, v in data["steps"].items()}
        self.instruments = data["instruments"]
        self.legacy_only = data["legacyOnly"]
        self.complications = data["complications"]

    def install(self, knowledge) -> None:
        """v2.4'te karşılığı olmayan eski aletleri kataloğa ekler (adı yazılabilsin, kontrol tanısın)."""
        for code, item in self.legacy_only.items():
            knowledge.instruments.setdefault(code, {**item, "legacy": True})

    def codes(self, text, catalog) -> list:
        """Motorun metninden ('A / B', 'A + B', 'steril malzeme (A)') v2.4 kodlarını sırayla çıkarır."""
        out = []
        for raw in CODE.findall(text or ""):
            code = self.instruments.get(raw, raw)
            if code in catalog and code not in out:
                out.append(code)
        return out

    def step_for(self, v1_step, engine_codes, catalog, complication=None):
        if complication and complication.get("complicationId") in self.complications:
            return self.complications[complication["complicationId"]]["step"]
        entry = self.steps.get(int(v1_step))
        if entry is None:
            return None
        if entry.get("sutureStep") and any(catalog[c].get("category") == "suture" for c in engine_codes):
            return entry["sutureStep"]
        return entry["step"]

    def translate(self, dev: dict, knowledge) -> dict:
        catalog = knowledge.instruments
        complication = dev.get("complication") or None
        mismanaged = dev.get("deviationType") == "COMPLICATION_MISMANAGED"
        engine = self.codes(dev.get("expected"), catalog)
        given = self.codes(dev.get("actual"), catalog)

        step = self.step_for(dev["stepId"], engine, catalog, complication)
        if step is None:
            return {**dev, "conflict": f"eski {dev['stepId']}. adımın v2.4 karşılığı tabloda yok",
                    "legacy": {k: dev.get(k) for k in ("stepId", "expected", "actual")}}
        v2 = knowledge.expected_instruments(step, dev.get("parameters"))

        wanted = next((c for c in engine if c in v2), engine[0] if engine else None)
        actual = next((c for c in given if c not in engine), given[0] if given else None)

        allowed = None
        if engine:
            allowed = set(engine)
            if not mismanaged:
                # Motorun beklediğiyle aynı türden (ör. başka bistüri) v2.4 alternatifi önerilmez; destek aletleri önerilebilir.
                kinds = {catalog[c].get("category") for c in engine}
                allowed |= {c for c in v2 if catalog.get(c, {}).get("category") not in kinds}
            allowed = sorted(allowed - {actual})

        conflict = None
        if engine and not mismanaged:
            if not set(engine) & set(v2):
                conflict = (f"çelişki: motorun beklediği {', '.join(engine)} v2.4'te {step}. adımda yok; "
                            "backend kendi klinik tablosunu kullanır")
            elif actual in v2:
                conflict = (f"çelişki: verilen {actual} v2.4'te {step}. adımda doğru sayılıyor; "
                            "backend kendi klinik tablosunu kullanır")

        # Komplikasyon metni motorun kendi verisidir: çelişen adımda da anlatılır, ama v2.4 metni verilmeden.
        context_only = bool(conflict and complication)
        if context_only:
            conflict = None

        return {
            **dev,
            "stepId": step,
            "expected": wanted,
            "actual": actual,
            "allowed": allowed,
            "noScenario": mismanaged,
            "contextOnly": context_only,
            "conflict": conflict,
            "context": [self.complication_chunk(complication, knowledge)] if complication else [],
            "legacy": {k: dev.get(k) for k in ("stepId", "expected", "actual")},
        }

    def complication_chunk(self, c: dict, knowledge) -> dict:
        def names(codes):
            return ", ".join(knowledge.instrument_name(self.instruments.get(x, x)) for x in codes or [])

        current = (c.get("currentAction") or {}).get("order")
        lines = [f"Komplikasyon: {c.get('title', '')}",
                 f"Ne oldu: {c.get('narrative', '')}",
                 f"Çözüm: {c.get('resolutionNote', '')}"]
        actions = c.get("actions") or []
        if actions:
            lines.append("Çözüm sırası:")
        for a in actions:
            mark = "  ← şu an beklenen" if a.get("order") == current else ""
            suture = f" + {names([a['requiredSuture']])}" if a.get("requiredSuture") else ""
            note = f" ({a['note']})" if a.get("note") else ""
            lines.append(f"{a.get('order')}. {a.get('title', '')}: {names(a.get('allowedInstruments'))}{suture}{note}{mark}")
        return {"id": f"legacy-v1:complication:{c.get('complicationId', '')}", "text": "\n".join(lines)}
